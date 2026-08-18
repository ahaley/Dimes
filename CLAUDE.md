# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Dimes is

Dimes is a self-hosted, single-tenant software change tracker "for the age of agentic
development." Problems, feature requests, and **latent observations** (signals captured from
inside a host app) flow into an observation inbox, get promoted into Change Requests, and move
through a fixed lifecycle gated by an elevated approval step. AI participates as a **first-class
Actor** but is **recommend-only** in this pass — it comments, never changes state.

`specs/spec.md` is the authoritative design doc — read it before making architectural decisions.
`specs/parking-lot.md` lists deferred features (don't build these unless asked): code-executing
agents, autonomous approval, CI/CD, session replay, configurable workflows, multi-tenancy.

## Build, run, test

The repo is three independently-built pieces: the .NET API/solution, the React `web` app, and
the TypeScript `sdk`.

**Backend (.NET 10, from repo root):**
```bash
dotnet build Dimes.slnx
dotnet test                                  # all xUnit tests
dotnet test --filter FullyQualifiedName~LifecycleServiceTests   # one test class
dotnet test -- --coverage                    # coverage (MTP extension; args after -- go to the runner)
dotnet run --project src/Dimes.Api --urls http://localhost:5080 # run the API (port the web dev proxy expects)
```
Tests are **xunit.v3**, which self-hosts on Microsoft Testing Platform rather than VSTest. Two
consequences worth knowing, because both fail loudly and confusingly if lost:
`"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json` is what lets `dotnet test` run an
MTP project at all (the .NET 10 SDK refuses the old VSTest path), and the test project must be an
`OutputType=Exe`. A v3 project needs none of the VSTest packages — no `Microsoft.NET.Test.Sdk`,
`xunit.runner.visualstudio`, or `coverlet.collector`; leaving them in selects the VSTest path and breaks
the run.

**Pass `Ct` to anything taking a `CancellationToken`.** `xUnit1051` requires it so a cancelled or
timed-out run stops promptly. `Ct` is a global `using static` alias for
`TestContext.Current.CancellationToken` (`TestCancellation.cs`) — the analyzer accepts any expression
there, and the full form at ~640 call sites pushed lines past 250 characters. New tests should use it;
`dotnet format analyzers --diagnostics xUnit1051` fixes omissions, but note it also reflows the
statements it touches, so re-check line lengths afterwards.

**A running API locks its own build output.** `dotnet build` fails with MSB3021/3027 ("file is locked
by: Dimes.Api") while an instance is running from `src/Dimes.Api/bin`. Stop it first, or build with
`-p:BaseOutputPath=<elsewhere>` to leave that directory alone.
Migrations run automatically on API startup (`db.Database.Migrate()` in `Program.cs`), so a fresh
checkout needs no manual DB step. The SQLite file defaults to `src/Dimes.Api/data/dimes.db`.

EF Core migrations (the `dotnet-ef` tool is pinned in `dotnet-tools.json`). **There are two
migration sets and a model change must be added to BOTH**, or `Database.Migrate()` aborts at startup
on the un-updated provider with `PendingModelChangesWarning` (a context has one model snapshot per
assembly):
```bash
dotnet tool restore
# SQLite (the tested default) — migrations live in Dimes.Infrastructure
dotnet ef migrations add <Name> --project src/Dimes.Infrastructure --startup-project src/Dimes.Api
# Postgres — separate set in Dimes.Infrastructure.Postgres (its own design-time factory)
dotnet ef migrations add <Name> --project src/Dimes.Infrastructure.Postgres --startup-project src/Dimes.Infrastructure.Postgres
```
EF backfills a new non-nullable column with the type default (e.g. `false` for a `bool`); if existing
rows should keep a different value, edit `defaultValue:` in **both** generated migrations to match the
property initializer. Verify a clean state with
`dotnet ef migrations has-pending-model-changes` against each set.

**Web (from `web/`):**
```bash
npm install
npm run dev        # Vite dev server; proxies /api and /openapi to http://localhost:5080 (override with DIMES_API)
npm run build      # tsc -b && vite build
npm run lint       # eslint
```
Run the API first — the web app has no mock backend.

**SDK (from `sdk/`):**
```bash
npm install
npm run build      # tsup → dist/ (cjs + esm + d.ts)
npm test           # vitest
```

## Architecture

### Backend layering (strict dependency direction)
- **`Dimes.Domain`** — entities (`Entities/`), enums (`Enums.cs`), the lifecycle engine
  (`Lifecycle/LifecycleService.cs`), and provider *interfaces* (`Providers/`). Pure domain, no
  EF/HTTP/DI dependencies, so it is trivially unit-testable.
- **`Dimes.Infrastructure`** — `DimesDbContext` (EF Core), migrations, and provider
  *implementations* (`Providers/`: Anthropic, OpenAI-compatible, GitHub). Registration extensions:
  `AddDimesPersistence`, `AddDimesProviders`.
- **`Dimes.Api`** — controllers (`Controllers/`), application services (`Services/`), and
  request/response DTOs + mapping (`Contracts/`). Composition root is `Program.cs`.

### The lifecycle engine is load-bearing — respect it
**All status changes must go through `LifecycleService`** (a registered singleton). It is the single
authority that (1) validates a transition is structurally legal, (2) enforces the role guard, (3)
mutates the entity, and (4) returns an `AuditEvent` the **caller must persist**. Never mutate a
`Status` field directly in a service or controller, and never replicate the transition rules in the
UI — the spec calls this out as a locked rule.

- Two state machines live here: `ChangeTransitions` (the change spine:
  `Captured → Triaged → Approved → InDevelopment → InReview → Done`, plus `Rejected`/`Duplicate`)
  and `ObservationTransitions` (`New → Clustered → Promoted | Dismissed`).
- **The approval gate (`Triaged → Approved`) and acceptance (`→ Done`) require `Maintainer`.** This
  is the elevated whitelist authority — the product's core RBAC rule. Other transitions require
  `Contributor`. `MemberRole` is an ordered enum, compared with `<`.
- Every mutating service method follows the pattern in `ChangeRequestService`: resolve
  `(actor, role)` via `MembershipResolver`, call the lifecycle method (or build an `AuditEvent`
  directly for create/edit), `db.AuditEvents.Add(...)`, then one `SaveChangesAsync`. The audit log
  is append-only and records **every** transition.

### The Actor model unifies humans and agents
Every participant is an `Actor` with `Type = Human | Agent`. Assignee, comment author, and
(future) approver are all `ActorId`. An Agent actor references an `LlmProviderConfig`. Agent
commentary is just a non-human actor driving an existing transition — parked agentic features add no
new states. Per-project roles live in `Membership` (one role per actor per project).

### Providers (behind thin interfaces)
`ILlmProvider` has four adapters: Anthropic Messages API (primary), OpenAI-compatible (OpenAI,
aggregators, local runners like Ollama/vLLM — *and* Gemini via Google's compatibility shim, no adapter
needed), Gemini (Google AI, `x-goog-api-key`), and Gemini via Vertex AI (minted OAuth2 bearer).
**A vendor only earns a native adapter when its auth model differs from a plain bearer token** — that
is the rule to apply before adding a fifth. `IScmProvider` has GitHub (read-only repo/PR links). Each
adapter gets its own `HttpClient`; they're registered as interface *sets*
(`AddTransient<ILlmProvider>(...)`) so callers select by provider type. Secrets are referenced
(`apiKeySecretRef`) and resolved via `ISecretResolver`, never stored plaintext. LLM commentary is
recommend-only — it posts a `Comment` with `Kind = AgentRecommendation` and must never change state.

**Secret references resolve four ways** (`ConfigurationSecretResolver`), in order: `Secrets:<section><name>` /
`SecretFiles:<section><name>` in configuration, then env `<prefix><name>` / `<prefix><name>_FILE`. The
`*File`/`_FILE` routes name a *path* and the resolver returns the file's contents, so callers never touch
the filesystem — that exists for credentials operators hold as files (a Vertex service-account JSON,
Docker/K8s secret mounts). Literal beats file within each tier, and configuration beats environment. A
configured-but-unreadable path throws rather than returning null, because "no secret configured" would send
the operator hunting in the wrong place.

**The section/prefix is a privilege boundary, not tidiness** — every `Resolve` call states a
`SecretPurpose`, and there is deliberately no single-argument overload because the omitted default would be
the dangerous one. `Operator` (the OIDC client secret, named in appsettings) resolves *unprefixed*;
everything a project Maintainer can type into a form is confined to its own section — `Secrets:Llm:<name>` /
env `DIMES_LLM_<name>`, and likewise `Notification:` / `DIMES_NOTIFICATION_` and `Scm:` / `DIMES_SCM_`.
Before this, one flat namespace meant "can configure an LLM provider" implied "can read every secret the
process can see": a Maintainer could put the OIDC client secret's reference name on an `OpenAICompatible`
config pointed at a host they control and the adapter would send it there as a bearer token. Containment
holds because the prefix is a fixed leading segment a reference can only extend — config keys have no
traversal, env names are suffixes — which is also why the reference itself is *not* charset-restricted.
When a reference misses but the same name is bound unprefixed, the resolver logs a migration warning naming
the setting to create; it does **not** surface that to the caller, because telling a Maintainer "that one
exists" rebuilds the very oracle this removed.

**Vertex additionally accepts a bare path as the value** — `GeminiVertexLlmProvider.ReadCredentialsJson`
reads the key file when the resolved credential isn't JSON, so the file routes are optional for that type.
Do **not** generalize this to the bearer-token adapters: the reference *name* comes from the provider form,
so a Maintainer can name any environment variable, and a token-shaped credential is sent verbatim to the
configured endpoint (any host, for `OpenAICompatible`). Vertex is safe because the contents never leave the
process — they're parsed locally into a Google-signed token scoped to `*.googleapis.com`, the same trust the
ADC option already grants. The path must be rooted, and a non-path value is refused *without being echoed*,
so a key pasted into that field never lands in a log.

**Provider scope is explicit and movable.** A provider is website-wide (`ProjectId == null`, available to
every project) or scoped to one project. `POST /api/llm-providers/{id}/scope` moves it, and authority is
**two-sided** — the caller must administer the source (`EnsureProviderAdminAsync`) *and* clear the bar for
creating in the destination (project admin, or site admin for website-wide); checking only the source would
let a project Maintainer publish their provider site-wide. Narrowing a scope is **refused while it would
strand an agent**: an Agent actor references a provider by id and nothing re-checks that assignment when the
provider moves, so `RequireInScope` also asserts it at the point of use. The `/providers` view always opens
on website-wide and picks scope explicitly — it sits under the site-admin Settings group, so inheriting the
last-visited project made the owning project invisible.

**Reasoning mode is always stated, never inherited.** `LlmCompletionRequest.Reasoning` (`Disabled` by
default) exists because the vendor default isn't stable across models: omitting Anthropic's `thinking` field
means "no thinking" on Sonnet 4.6 but "adaptive" on Sonnet 5 — and a request's token budget covers reasoning
*and* the answer, so changing only a config's model id could start spending the budget on reasoning and
return nothing. Both call sites set it explicitly. `OpenAICompatible` ignores it (no field the many vendors
behind that shim agree on); the Gemini adapters ignore it too.

**An empty completion is an error, not an empty comment.** `LlmHttp.RequireText` fails with the vendor's
stop reason instead of storing a blank `AgentRecommendation` — the adapters used to coalesce a missing
completion to `string.Empty`, which is indistinguishable from the model having nothing to say. `LlmHttp` also
surfaces vendor error bodies; never go back to `EnsureSuccessStatusCode()`, which discards the one actionable
detail (bad key, unknown model, rejected parameter).

Two things to keep straight when touching providers:
- **Model ids are never hardcoded.** `LlmProviderConfig.Model` is free text, and `ILlmModelCatalog` is
  an *optional* capability (test with `provider is ILlmModelCatalog`) that powers the "Discover models"
  probe. Adding support for a newly released model should require no code change at all. All four
  adapters implement it today; the interface stays optional because a minimal local runner may expose no
  listing route. Two Vertex-specific things to keep straight: its catalog is `publishers.models.list`,
  which exists **only on `v1beta1`** (verified against the discovery document — `v1` has no such method),
  so that adapter deliberately speaks `v1` for generation and `v1beta1` for listing; and unlike the
  credential-scoped listings it is a *published catalog*, so a listed id can still be unavailable to a
  given project or region — the UI says so rather than implying availability. Vertex results are filtered
  to the `gemini-` family because the adapter only speaks the Gemini `generateContent` body and the
  catalog has no capability field to filter on; that is a family prefix, not a list of known ids.
- **`ProviderUrlValidator` is per provider type.** OpenAI-compatible stays permissive because
  localhost/private-LAN runners are the point of it; the vendor types are pinned to the vendor's domain
  so a stored key can't be redirected to an attacker's host. Build connections through
  `LlmProviderConfig.ToConnectionAsync` — it pairs the call-time re-validation with the secret
  resolution so the two can't drift apart.

### Persistence conventions
- Enums are stored as **strings** (human-readable, ordinal-stable). Note the consequence: a DB
  `ORDER BY` on `Priority` sorts alphabetically, not by severity — sort in memory when severity
  order matters (see `ExportInDevelopmentAsync`).
- Actor-referencing FKs use `DeleteBehavior.Restrict` to avoid multiple-cascade-path conflicts on
  stricter providers (Postgres). SQLite is the tested default; the same model targets Postgres for
  heavier installs.
- Varied signal shapes (`Observation.Payload`, `ContextMetadata`) are JSON columns.

### Frontend
React 19 SPA (Vite + Tailwind v4, TanStack Query, dnd-kit for the board). `src/api/client.ts` is a
hand-written typed fetch client; `src/api/types.ts` is a **hand-written mirror** of the C# contracts
(a future slice may generate it from the OpenAPI doc — keep the two in sync when you change a DTO).
`src/api/hooks.ts` wraps the client in React Query hooks. UI is organized as `features/` (board,
inbox, detail, actors, providers, settings) over shared `components/`. `src/lifecycle.ts` mirrors
the allowed-transition affordances for the UI — but the backend `LifecycleService` remains the
enforcement authority.

### Capture SDK (`sdk/`)
Framework-agnostic, dependency-light TypeScript that embeds in arbitrary host apps to capture latent
+ explicit signals (`widget.ts`, `friction.ts`, `context.ts`, `integrations.ts`) and POST them to
the API as Observations. Isolated from the frontend's framework choice on purpose.

## Conventions
- C#: nullable reference types and implicit usings are enabled across all projects. Services use
  primary-constructor DI (`class FooService(Dep a, Dep b)`). Domain exceptions
  (`BadRequestException`, `NotFoundException`, `ForbiddenException`, lifecycle exceptions) are
  translated to ProblemDetails by `GlobalExceptionHandler`; the web `ApiError` surfaces
  status/title/detail (e.g. 403 guard failures) to the user.
- Enums serialize as strings over the wire (`JsonStringEnumConverter`), matching the TS string-union
  types.
