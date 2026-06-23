# Change Request Identifier — Options & Recommendation

> Investigation for change `48c7e8ed-1887-4ef1-8468-2349c58747a8`. This is a design recommendation,
> not an implemented change. It surveys how a Change Request should be identified, weighed against the
> four goals below, and recommends a scheme plus a migration path.

## Why this matters

Today a Change Request is identified **only by its `Guid Id`** (`Entity.Id`, a random v4 GUID). There
is no human-readable key, number, or slug, and a Project has a `Name` but **no short code**. The one
place we already shorten the GUID is work-order branch names, which take the **first 8 hex digits**
(`change/35364863-observe-project-side-list-under-websockets`). That convention is informal, not
stored, and not project-scoped.

GUIDs are great for the database and API (globally unique, collision-free, safe to generate
client-side) but poor for humans: `35364863-…` is unpronounceable, unmemorable, carries no project
context, and two of them are visually indistinguishable at a glance. As Dimes grows toward request
**composition** (epics, request pipes, blockers — see `specs/parking-lot.md`), people will need to
*refer* to changes constantly ("DIMES-142 blocks DIMES-156"), and a GUID makes that conversation
impossible.

## Evaluation criteria

A good identifier should score well on all four:

1. **Quick, accurate visual recognition** — short, distinct, low transposition risk, readable aloud.
2. **Identifies the owning project** — you can tell which project a change belongs to from the id alone.
3. **Usable as a slug** — URL/branch/filename-safe, stable, no escaping.
4. **Discoverable for composition** — supports referencing changes from epics, request pipes, and
   blockers; ideally hints at hierarchy/relationships.

The `Guid Id` stays as the immutable **primary key and API identity** in every option below — we are
choosing a *human-facing display key*, layered on top, never replacing the PK.

---

## Options

### Option A — Raw GUID (status quo)

`35364863-7c1f-4e0e-9b6a-2a1f9c0d4e7a`

| Criterion | Score | Notes |
|---|---|---|
| Visual recognition | ✗ | 36 chars, no two are distinguishable at a glance, not sayable. |
| Project belonging | ✗ | Carries no project context. |
| Slug | △ | URL-safe but ugly and long. |
| Composition | ✗ | Referencing "the GUID that blocks the other GUID" is unworkable. |

**Verdict:** Fine as the PK, unacceptable as the thing humans see and cite.

### Option B — Short GUID prefix (current branch practice)

`35364863` (first 8 hex)

| Criterion | Score | Notes |
|---|---|---|
| Visual recognition | △ | Shorter, but still opaque hex; collision risk grows across many changes. |
| Project belonging | ✗ | No project context. |
| Slug | ✓ | Already used in branch slugs. |
| Composition | ✗ | No ordering or hierarchy; no project scoping for relations. |

**Verdict:** A reasonable *abbreviation* of the GUID, but it inherits the GUID's opacity and adds
collision ambiguity. Keep it only as a fallback display of the PK, not the primary key.

### Option C — Project-key + per-project sequence (Jira-style) — **recommended**

`DIMES-142`, `WEB-7`

A short uppercase **project key** (`Project.Key`, e.g. `DIMES`) plus a **per-project, monotonically
increasing number** assigned at creation (`ChangeRequest.Number`). The display key is
`{Project.Key}-{Number}`.

| Criterion | Score | Notes |
|---|---|---|
| Visual recognition | ✓ | Short, sayable ("dimes one-forty-two"), numbers are familiar and ordered. |
| Project belonging | ✓ | The prefix *is* the project. |
| Slug | ✓ | `dimes-142` lowercased is a clean slug/branch/filename. |
| Composition | ✓ | Stable, citable handles; epics/pipes/blockers reference keys directly. |

**Cost:** a `Key` column on Project (unique, validated `^[A-Z][A-Z0-9]{1,9}$`), a per-project counter,
and atomic number assignment on create (see *Implementation* below). This is the only option that
scores well on **all four** criteria.

### Option D — Short opaque code (Crockford Base32 / nanoid)

`C7K2Q`, `4F9TbX`

A short random or sequence-derived code from a human-friendly alphabet (Crockford Base32 excludes
I/L/O/U to avoid confusion).

| Criterion | Score | Notes |
|---|---|---|
| Visual recognition | △ | Compact and collision-resistant, but still semantically opaque; no order. |
| Project belonging | ✗ | No project context unless prefixed (then it converges on Option C). |
| Slug | ✓ | Clean. |
| Composition | △ | Citable, but no inherent ordering/hierarchy. |

**Verdict:** Good when you want compact globally-unique public handles (e.g. short links) but weaker on
project belonging and ordering than Option C. Worth keeping in mind for *public-facing* share URLs.

### Option E — Hierarchical key for composition (extends C)

`DIMES-12` (epic) → `DIMES-12.3` (child) → `DIMES-12.3.1` (sub-task); relations expressed as typed
edges between keys.

This is **not a competing base scheme** but a layer on top of Option C for when composition lands. Two
sub-choices:

- **Flat keys + typed relations (recommended):** every change keeps a flat `DIMES-142`; epics, pipes,
  and blockers are **typed edges** in a relations table (`(fromKey, toKey, relation)` where
  `relation ∈ {parent-of, blocks, precedes}`). A change can move between epics without its key
  changing — keys stay stable, which matters because they get cited in branches, commits, and chat.
- **Dotted hierarchical keys:** `DIMES-12.3` encodes the parent in the key. Reads nicely but the key
  *changes* if a change is re-parented, breaking every prior reference — avoid for anything citable.

**Verdict:** Adopt **flat keys + typed relations**. Composition is expressed in data, not baked into
the identifier, so identifiers stay immutable and quotable.

---

## Recommendation

Adopt **Option C (project-key + per-project sequence) with flat keys**, and model composition as
**typed relations between keys (Option E, flat variant)** when epics/pipes/blockers are built.

Concretely:

- Keep `ChangeRequest.Id` (GUID) as the immutable PK and API/route identity — no behavioral change to
  existing endpoints.
- Add `Project.Key` (short uppercase code, unique, immutable once set).
- Add `ChangeRequest.Number` (int, per-project sequence, assigned at create).
- Derive the **display key** `{Project.Key}-{Number}` (e.g. `DIMES-142`) — show it on board cards, the
  detail header, focus rail, audit lines, and exports; make it the copy-able handle and the slug source
  (`dimes-142`).
- When composition arrives, add a relations table keyed by change keys with a `relation` discriminator;
  epics are a parent-of edge (or a lightweight Epic entity), request pipes are ordered `precedes` edges,
  blockers are `blocks` edges.

### Why this wins

It is the only option that satisfies all four goals at once: it’s short and sayable (recognition),
the prefix names the project (belonging), it lowercases to a clean slug, and flat immutable keys are
exactly what composition needs to reference. It also matches the mental model every developer already
has from Jira/Linear/GitHub, so it needs no explanation.

---

## Implementation sketch (for a future change — not done here)

1. **Schema** (migration in **both** `Dimes.Infrastructure` and `Dimes.Infrastructure.Postgres`):
   - `Project.Key` — `string`, unique index, validated `^[A-Z][A-Z0-9]{1,9}$`. Backfill existing
     projects from an uppercased, de-duplicated derivation of `Name` (e.g. first letters / first word),
     resolving collisions with a numeric suffix.
   - `ChangeRequest.Number` — `int`. Unique index on `(ProjectId, Number)`.
2. **Number assignment** — assign at creation in `ChangeRequestService.CreateAsync` / `CreateManyAsync`.
   To stay correct under concurrency, derive the next number atomically (a per-project counter row
   updated in the same transaction, or `MAX(Number)+1` guarded by the `(ProjectId, Number)` unique
   index with a retry). Backfill existing changes per project ordered by `CreatedAt`.
3. **Contracts/UI** — add `Key` to `ProjectDto`, `Number` (+ computed `DisplayKey`) to
   `ChangeRequestDto`; mirror in `web/src/api/types.ts`; render `DISPLAYKEY` on `ChangeCard`,
   `ChangeDetail`, `FocusView`, audit, and the In-Development export. Use the lowercased key as the slug
   in branch-name suggestions (replacing the current first-8-hex convention).
4. **Composition (later)** — a `ChangeRelation(FromChangeId, ToChangeId, RelationType)` table with
   `RelationType ∈ {ParentOf, Blocks, Precedes}`; surface relations in the detail view and validate
   against cycles for `Blocks`/`Precedes`.

### Open questions for product

- Should `Project.Key` be user-chosen at project creation (best), or always derived? (Recommend: user
  enters it, pre-filled from the name, validated unique.)
- Are change numbers ever reused after deletion? (Recommend: never — numbers are permanent, matching
  Jira, so citations never become ambiguous.)
- Is the display key case-sensitive in lookups? (Recommend: store uppercase, look up
  case-insensitively, so `dimes-142` and `DIMES-142` resolve the same.)
