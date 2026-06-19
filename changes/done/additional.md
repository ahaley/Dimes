# Work order — implement In-Development changes (Dimes)

## Objective

You are Claude Code working in this repository. Implement every change request in the
"Changes" section below against this codebase. This file is the full task list — keep
going until each change is implemented and merged, or explicitly recorded as blocked.

## How to work

1. **Record the integration branch.** Note the branch you start on (e.g. `main`); call it
   the *integration branch*. Every change branches from it and merges back into it.
2. **One branch per change**, in order. Create a branch off the *current* integration
   branch using the `Branch:` name given for each change below.
3. **Implement only that change**, then **verify before committing**: the project must
   build and the relevant tests must pass. Never commit a broken change.
4. **Commit on the branch.** First line is the change title, then a blank line, then
   `Dimes change <id>`. Keep unrelated edits out of the commit.
5. **Merge back.** Switch to the integration branch and run
   `git merge --no-ff <branch>` so each change is one reviewable merge commit. Resolve
   any conflicts and re-verify the build after merging.
6. **Sequence matters.** Branch each subsequent change from the *updated* integration
   branch so later changes build on earlier ones.
7. **If a change can't be completed** (won't build, tests fail, unresolvable conflict),
   leave its branch unmerged, check it off as blocked with a one-line reason, and
   continue with the remaining changes.
8. Work autonomously through the whole list; pause only if a change is too ambiguous to
   implement safely. When finished, report what merged and what's blocked.

## Changes

### 1. Explanation of actor delete lock (priority: Medium)

Explain to the user in the actor section why an actor is locked from deletion.

- Change id: `a202c653-5f0a-4382-a535-230c58a60ccb`
- Branch: `change/a202c653-explanation-of-actor-delete-lock`
- [x] Implemented, verified, committed, merged

### 2. Add a dark mode (priority: Low)

Introduce a dark mode toggle at the top right.

- Change id: `7c664319-65ea-424d-93be-b7b1b5ff3577`
- Branch: `change/7c664319-add-a-dark-mode`
- [x] Implemented, verified, committed, merged

