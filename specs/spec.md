# Dimes — First-Pass Specification

> Deferred ideas live in `parking-lot.md`.

Dimes is a software change tracker for the age of agentic development. Problems, feature
requests, and **latent observations** by software users flow into a change list; an
elevated role whitelists changes into active development; AI participates as a first-class
actor (recommend-only in the first pass). It is **open-source, self-host-first,
single-tenant**, and keeps data and integrations inside the operator's own infrastructure.

## Goals (first pass)

A thin but complete loop, shipped as a **single self-hosted, API-first deployable**:
capture → observation inbox → change lifecycle with an elevated approval gate →
recommend-only LLM commentary → read-only GitHub link. No code-executing agents, no
autonomous approval, no CI/CD — those are in the parking lot.

## Product shape

- **Open-source, self-host-first, single-tenant.** One deployable an org runs in its own
  network; both "control" and "data" concerns live together, so the Seq adapter and
  (future) repo access reach internal services directly. No multi-tenancy, no vendor
  cloud, no credential-trust problem.
- **Single deployable, "one command up"** — self-host ergonomics are a feature.
- **API-first** — the SDK and any future clients talk to the same API; keeps a later
  control/data-plane split or hosted offering reversible without building it now.
- **Nothing phones home** — storage, observation analytics, and LLM orchestration run
  locally. *Caveat:* LLM calls leave the network unless a local model is configured (see
  Providers).

## Tech stack

- **Backend:** ASP.NET Core (C#) — API, lifecycle engine, RBAC/Actor model, audit log,
  `ObservationSource` adapters. Native Seq/Serilog fit. LLM calls are plain HTTP; future
  agent execution is *orchestrated external processes*, not reimplemented in C#.
- **Frontend:** React SPA, consuming a **typed client generated from the ASP.NET OpenAPI
  spec** (end-to-end C#→TS type safety).
- **SDK:** TypeScript, framework-agnostic, dependency-light; embeds in arbitrary host web
  apps; isolated from the frontend framework choice.
- **Components (OSS-safe):** headless React libs — shadcn/ui (Radix + Tailwind), TanStack
  Table (change list), dnd-kit (lifecycle board). **No Kendo UI** (commercial — would
  force a license on every self-hoster).
- **Database:** SQLite default (single-file, zero-dependency), PostgreSQL optional for
  heavier installs. EF Core abstracts the provider; SQLite is the tested default.

## Capture — observation sources

Capture is a set of **pluggable sources behind one `ObservationSource` interface**, not
"the SDK plus integrations":

- **Source #1 — SDK** (client-side): latent + explicit signals from inside the host app.
- **Source #2 — Seq / structured-log adapter** (server-side): runtime exceptions, pulled
  via Seq's HTTP query API (`@Level in ['Error','Fatal']`) or pushed via Seq apps/webhook.
  Optional, off by default. Exceptions aggregate by type + stack signature → one candidate
  per fingerprint with an occurrence count.

**Boundary:** Dimes consumes meaningful/recurring signals into a curated change list; it
does **not** mirror the log firehose or become an error tracker.

**Pass-1 capture surface** (thin, spanning the spectrum):
- Explicit feedback widget (report a problem / suggest an idea).
- Technical signals — front-end errors via the SDK; server-side exceptions via Seq.
- A couple of high-confidence friction signals (rage clicks, flow abandonment).
- Context metadata on everything (route, app version, role, breadcrumbs, device).

Deferred capture (parking lot): session replay, screenshots, full behavioral telemetry,
ML clustering, wider adapter catalog.

**Cross-cutting constraints:** privacy/consent (opt-in, scrubbing, sampling); SDK
performance budget (async, batched, sampled, hard size/CPU ceiling); volume/cost
(sampling + aggregation are survival); component-level attribution (enables the parked
component-mount demo).

## Change lifecycle

Two linked state machines, plus the **Actor** abstraction.

**Actor abstraction (load-bearing):** every participant is an Actor that is **human or
agent** and holds roles. Parked agentic features are not new states — just a non-human
actor driving an existing transition (agent commentary now; agent execution/approval
later).

**Observation inbox:** `New → Clustered (LLM-assisted dedup) → Promoted | Dismissed`. A
promoted observation spawns/links a Change Request and stays attached as **evidence**.

**Entry policy (confidence-based):** passive signals (Seq, behavioral) enter the inbox and
must be promoted; explicit human problem/feature submissions start directly at `Captured`.

**Change lifecycle (the spine):**

```
Captured → Triaged → Approved/Whitelisted → In Development → In Review → Done
                          (elevated gate)                   (demo attaches    (parked) → Deployed
                                                             here, parked)
   any → Rejected / Duplicate          Done → reopen → In Development
```

- **Approved / Whitelisted** is the gate — **elevated role only**. Before it = candidate;
  after it = committed work.

**Roles (humans only in pass-1):** Reporter → Contributor → **Maintainer (elevated)**.

| Transition | Driver |
|---|---|
| → Captured | anyone / any source (Reporter) |
| Captured → Triaged | Triager / Contributor |
| Triaged → **Approved** | **Maintainer only** (whitelist authority) |
| Approved → In Development → In Review | Assignee |
| In Review → Done | Maintainer / reviewer |
| any → Rejected / Duplicate | Triager+ |

**Locked rules:** audit log on every transition; guards in centralized transition logic
(`LifecycleService`), not the UI; one **fixed** workflow (configurable workflows parked);
SCM linkage is a manual read-only repo/PR link (auto-sync parked).

## Providers (behind thin interfaces)

- **`LlmProvider` — two adapters:**
  - **Claude (Anthropic Messages API)** — primary. Default model **Sonnet 4.6**,
    configurable to **Opus 4.8** / **Haiku 4.5**. Bring-your-own-key.
  - **OpenAI-compatible endpoint** — covers OpenAI *and* local runners (Ollama / vLLM /
    LM Studio) via a configurable base URL, so privacy-sensitive operators keep
    observation text on-network.
  - Recommend-only: cluster/dedupe observations, summarize candidates, suggest
    priority/dupes, comment on changes. **Never changes state.**
- **`ScmProvider` — GitHub** (pass-1): read-only repo/PR link + context pull. No build
  actions. GitLab / Azure DevOps / Bitbucket parked.

## Data model

Organizing principle: the **Actor** table unifies humans and agents, so
authorship/assignment/approval are uniform.

```
Project ──┬─< ChangeRequest ──┬─< Comment            (author = Actor: human OR agent)
          │                   ├─< AuditEvent          (append-only, every transition)
          │                   ├─< ScmLink             (GitHub repo/PR, read-only)
          │                   └── DuplicateOf ─┐ (self-ref)
          ├─< Observation >── ChangeRequest  (evidence link, nullable; many→one)
          │        │
          │   ObservationSource (SDK | Seq | …)
          ├─< Membership (Actor × Role)        Role: Reporter | Contributor | Maintainer
          ├─< LlmProviderConfig  (Anthropic | OpenAI-compatible)
          └─< ScmProviderConfig  (GitHub)

Actor (Human | Agent) — an Agent references an LlmProviderConfig
```

- **`Actor`** — `Type` (Human|Agent); Human → auth identity, Agent → FK to
  `LlmProviderConfig`. Assignee / comment author / future approver are all `ActorId`.
- **`Membership`** — `Actor × Project × Role`; Maintainer holds the whitelist authority.
- **`ObservationSource`** — `Type` (SDK|Seq|…), config + secret ref, per project.
- **`Observation`** — `SourceId`, `Kind`, `Status` (New|Clustered|Promoted|Dismissed),
  `Payload` (JSON), `ContextMetadata` (JSON), `Fingerprint` + `OccurrenceCount` +
  `First/LastSeen`, nullable `ChangeRequestId` (evidence).
- **`ChangeRequest`** — `Kind` (Problem|Feature|Observation-driven), `Status`, `Priority`,
  `AssigneeActorId`, `CreatedByActorId`, self-ref `DuplicateOfId`.
- **`Comment`** — `AuthorActorId`, `Body`, `Kind` (human | agent-recommendation).
- **`AuditEvent`** — append-only: `EntityType`/`EntityId`, `ActorId`, `FromStatus`,
  `ToStatus`, `Action`, `Reason`, `Timestamp`.
- **`ScmLink`** — `ChangeRequestId`, `Provider` (GitHub), repo/PR URL, context snapshot.
- **`LlmProviderConfig` / `ScmProviderConfig`** — type, base URL/model, **secret
  references** (encrypted at rest, never plaintext).

Decisions: lifecycle is code not data (`Status` enum + `LifecycleService`); JSON columns
for varied signal shapes; lightweight `(ProjectId, Fingerprint)` clustering; Project is
first-class even single-tenant; secrets referenced + encrypted at rest.

## Verification (end-to-end)

1. **Capture (explicit)** — submit via the React UI → lands at `Captured`.
2. **Capture (SDK)** — fire a test signal from the TS SDK in a sample host app → appears
   as an Observation.
3. **Capture (Seq)** — emit an `Error` to a local Seq instance → the adapter ingests it;
   identical signatures aggregate.
4. **Promote** — promote an Observation → a Change Request is created with the observation
   attached as evidence.
5. **Lifecycle + RBAC** — drive `Captured → Triaged`; confirm a non-elevated actor is
   **blocked** at `Triaged → Approved` and a Maintainer can cross; run to `Done`; verify
   the audit log records every transition.
6. **Agentic commentary** — trigger recommend-only commentary via the **Claude** adapter;
   confirm it posts a comment and does **not** change state. Repeat against a **local
   OpenAI-compatible endpoint** to prove the data-stays-local path.
7. **SCM link** — attach a **GitHub** repo/PR link and pull context (read-only).
8. **Automated tests** — unit tests on transition guards (illegal transitions rejected) +
   an integration test for capture → promote → approve → done.
