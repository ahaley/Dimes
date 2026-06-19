# Dimes — Parking Lot

Ideas that are intentionally **out of scope for the first pass**. Each entry: what it is
and why it's deferred. Promote to scope deliberately, not by drift.

## Agentic capabilities

- **Agents that execute code changes against the repo** — an agent opens branches/PRs and
  edits the codebase. Deferred: largest security + cost + sandboxing surface; depends on a
  settled change lifecycle first. Intended approach when promoted: *orchestrate an external
  coding agent* (e.g. Claude Code / Agent SDK) as a subprocess rather than reimplement the
  agent loop in C#. Slots into the `In Development` state as an Agent assignee.
- **Agent-as-autonomous-approver** — an AI holding an elevated role that can *whitelist*
  changes without a human. Deferred: governance/trust model must mature first; pass-1 is
  recommend-only. Slots into the `Triaged → Approved` gate as an Agent actor.
- **Full CI/CD propagation to a live service** — automated deploy on change execution.
  Deferred: superseded in priority by the lighter "pluggable demo" idea. Slots in after
  `Done` as a `Deployed` state.

## Pluggable demo (preview a change without full CI/CD)

Deferred as a *feature*, but the pass-1 SDK is deliberately chosen to make the lightest
model cheap later. Spectrum, lightest → heaviest:

| Model | Isolates | Infra cost | Works for |
|---|---|---|---|
| Component mount (via SDK) | the changed component | low — reuses pass-1 SDK | UI/component-level changes |
| Feature flag in live app | the change, behind a flag | medium — flag plumbing | anything already deployed |
| Vercel-style preview env | the whole app, per change | high — build + host + data | web apps shaped right |

**Vercel-style per-change deployment** (the heavy end):
- *How it works* — Git host webhook fires a build per push/PR commit; each build deploys
  to its own immutable, uniquely-addressed URL; previews are the whole app (frontend +
  serverless) wired to non-prod data; the URL is posted back for review. Cheap per-change
  because of build caching, scale-to-zero serverless, and externalized state.
- *Why parked* — (1) assumes a Vercel-shaped app (stateless frontend + serverless +
  external state); monoliths, stateful backends, and desktop/mobile/embedded don't get a
  URL preview. (2) The data problem: previews need prod data (risky), seeded fixtures
  (maintenance), or ephemeral branched DBs (another integration). (3) It's infra, not app
  logic — owning build pipeline, hosting, routing, TTL/cleanup, secrets is a real platform
  investment.

**Strategic note:** the *component-mount* model reuses the observation SDK as the
injection point — "mount just the changed piece into an already-running app" rather than
"deploy the whole app per change." Lighter, sidesteps the data/full-build problem, but
only demos changes expressible as a swappable component. This is the strongest reason the
SDK is a pass-1 anchor. Component-level attribution in capture (recording *which
component* a signal came from) is what enables this later.

## Capture breadth

- **Rich passive capture** — session replay, screenshots / DOM snapshots, full behavioral
  telemetry. Deferred: privacy + volume cliffs; prove the thin loop first.
- **ML / automatic clustering of signals** — auto-grouping raw signals into candidates.
  Deferred: start with simple `(ProjectId, Fingerprint)` thresholding + LLM-assisted
  synthesis.
- **Richer observation clustering** — a first-class `ObservationCluster` entity beyond
  fingerprint aggregation. Deferred.
- **Wider observation-source adapter catalog** — Sentry, Application Insights,
  OpenTelemetry, CloudWatch, GitHub-as-source. Deferred: SDK + Seq adapter prove the
  `ObservationSource` interface first.

## Providers

- **Additional LLM providers** — Gemini and other clouds beyond Claude / OpenAI-compatible.
- **Additional SCM providers** — GitLab, Azure DevOps, Bitbucket. Azure DevOps is the
  likely first follow-up given the .NET context. All sit behind the pass-1 `ScmProvider`
  interface.

## Platform & distribution

- **Multi-platform clients** — desktop, mobile, embedded shells beyond the website.
  Deferred: API-first design in pass-1 keeps the door open.
- **Hosted / managed offering + multi-tenancy** — running Dimes as a SaaS for others.
  Deferred: self-host-first decided; API-first keeps this reversible.
- **Split-plane customer-side connector** — hosted control plane + local data/agent plane.
  Deferred: only relevant if a hosted offering is later pursued.
- **OSS license choice** — permissive vs copyleft vs source-available; decide before any
  public release. Deferred decision, not forgotten.

## Lifecycle & integrations

- **Configurable / per-project workflows** — Jira-style custom lifecycles. Deferred:
  pass-1 hard-codes one lifecycle behind centralized transition logic.
- **Auto-sync SCM state → lifecycle** — a merged/closed PR automatically advances the
  change. Deferred: pass-1 is a manual repo/PR link only.

## Hardening (deferred but known)

- **Authentication / identity** — pass-1 has no auth; API actions take an `ActorId` in the
  request as a stand-in and the SPA has an "acting as" selector. A real identity/auth layer
  (and binding the acting actor to a session) is required before any real deployment.
- **Idempotent cross-client aggregation** — observation fingerprint aggregation is a
  read-then-insert and can race when *different* clients post the same fingerprint
  concurrently (counts become approximate). The SDK serializes its own deliveries so a single
  client aggregates correctly; hardening the server (filtered unique index on
  `(ProjectId, Fingerprint)` for open statuses + upsert/retry) would make it exact.
