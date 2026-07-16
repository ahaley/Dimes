using System.Text.RegularExpressions;

namespace Dimes.Domain.WorkOrders;

/// <summary>Reads back the commit contract the export itself specifies — see step 4 of
/// <see cref="SystemInstructionDefaults.ExportWorkOrder"/>, whose <c>Dimes change &lt;id&gt;</c> line is a
/// wire format, not prose. A full-GUID trailer is authoritative; a branch name is a fallback, and only ever
/// within a single work order.</summary>
public static partial class WorkOrderTrailer
{
    /// <summary><c>Dimes change &lt;guid&gt;</c> on a line of its own. Anchored per line so prose that merely
    /// mentions the phrase ("as discussed in Dimes change 1f2e… we should…") is not a claim of completion.
    /// A trailing period is tolerated — some tools reflow trailers.</summary>
    [GeneratedRegex(
        @"^[ \t]*Dimes[ \t]+change[ \t]+(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})[ \t]*\.?[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TrailerRegex();

    /// <summary>The <c>change/&lt;id8&gt;-&lt;slug&gt;</c> branch name the export mints (see
    /// <c>ChangeRequestService.ExportInDevelopmentAsync</c>).</summary>
    [GeneratedRegex(
        @"^change/(?<id8>[0-9a-fA-F]{8})-",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BranchRegex();

    /// <summary>Every distinct change id claimed by a commit message, in first-seen order. A commit may
    /// legitimately claim more than one change. The match timeout is defensive: both patterns are linear,
    /// but the input arrives on an anonymous endpoint.</summary>
    public static IReadOnlyList<Guid> ParseTrailers(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        var ids = new List<Guid>();
        foreach (Match match in TrailerRegex().Matches(message))
        {
            if (Guid.TryParse(match.Groups["id"].Value, out var id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>The 8-hex id prefix a Dimes-minted branch name carries, lowercased, or null if the branch
    /// isn't one of ours. Callers must treat this as a last resort and require a unique match within the
    /// work order — an 8-hex prefix is not an identity.</summary>
    public static string? ParseBranchIdPrefix(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        var match = BranchRegex().Match(branch.Trim());
        return match.Success ? match.Groups["id8"].Value.ToLowerInvariant() : null;
    }
}
