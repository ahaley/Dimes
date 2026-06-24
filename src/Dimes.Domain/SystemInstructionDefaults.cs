namespace Dimes.Domain;

/// <summary>Built-in default text for each <see cref="SystemInstructionKind"/>. These are the values an
/// install starts from: seeded into the per-project system-instructions table on startup, and used as the
/// fallback when a project has no row for a kind. Editing the seeded row (or a future UI) overrides the
/// default without changing this code.</summary>
public static class SystemInstructionDefaults
{
    /// <summary>The In-Development export "work order" guidance body — the Objective + How-to-work sections
    /// inserted between the generated title and the generated change list. Must stay in lockstep with the
    /// export renderer's surrounding scaffolding in <c>ChangeRequestService.ExportInDevelopmentAsync</c>.</summary>
    public const string ExportWorkOrder =
        """
        ## Objective

        This file is a work order: implement every change request in the "Changes" section below
        against this repository, until each is merged or recorded as blocked. The changes differ in
        nature — engineering, design, docs, infrastructure — so size up each one and bring (or
        delegate to a specialized agent with) the expertise it actually needs.

        ## How to work

        1. **Record the integration branch.** Note the branch you start on (e.g. `main`); call it
           the *integration branch*. Every change branches from it and integrates back into it.
        2. **One branch per change**, in order. Create a branch off the *current* integration
           branch using the `Branch:` name given for each change below.
        3. **Implement only that change**, then **verify before committing**: confirm it works the
           way that change can be checked — the project builds and its tests pass for code, the result
           renders and behaves correctly for UI or design, content is accurate for docs. Never commit
           a change you haven't verified.
        4. **Commit on the branch.** First line is the change title, then a blank line, then
           `Dimes change <id>` (this links the commit back to the request). Keep unrelated edits out
           of the commit.
        5. **Integrate back.** Switch to the integration branch and merge the change as one
           reviewable unit (e.g. `git merge --no-ff <branch>`). Resolve any conflicts and re-verify
           after merging.
        6. **Sequence matters.** Branch each subsequent change from the *updated* integration branch
           so later changes build on earlier ones.
        7. **If a change can't be completed or verified** (it doesn't work, conflicts can't be
           resolved, or it needs a decision you can't make), leave it unintegrated, check it off as
           blocked with a one-line reason, and continue with the remaining changes.
        8. Work autonomously through the whole list; pause only if a change is too ambiguous to carry
           out safely. When finished, report what integrated and what's blocked.
        """;
}
