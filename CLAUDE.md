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
dotnet run --project src/Dimes.Api --urls http://localhost:5080 # run the API (port the web dev proxy expects)
```
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
`ILlmProvider` has two adapters (Anthropic Messages API as primary, OpenAI-compatible for OpenAI +
local runners like Ollama/vLLM). `IScmProvider` has GitHub (read-only repo/PR links). Each adapter
gets its own `HttpClient`; they're registered as interface *sets* (`AddTransient<ILlmProvider>(...)`)
so callers select by provider type. Secrets are referenced (`apiKeySecretRef`) and resolved via
`ISecretResolver`, never stored plaintext. LLM commentary is recommend-only — it posts a `Comment`
with `Kind = AgentRecommendation` and must never change state.

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
