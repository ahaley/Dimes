namespace Dimes.Api.Services;

/// <summary>The one place that decides whether a string is safe to persist as an <c>ScmLink.Url</c>.
/// Shared by the manual link endpoint and work-order ingest so the scheme guard can't drift between
/// the path a human uses and the path an agent uses.</summary>
public static class ScmUrlValidator
{
    /// <summary>True if the URL is an absolute http(s) URL. Stored URLs are rendered as an
    /// <c>&lt;a href&gt;</c> in the SPA, so anything else — notably <c>javascript:</c> — could be planted
    /// and then executed in a viewer's authenticated session when clicked.</summary>
    public static bool IsValid(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    /// <summary>Throws <see cref="BadRequestException"/> unless the URL passes <see cref="IsValid"/>.
    /// Use where a bad URL is a caller error; ingest instead skips invalid URLs, since one bad link in a
    /// batch report shouldn't reject the agent's whole run.</summary>
    public static void Require(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new BadRequestException("SCM link URL is required.");
        }
        if (!IsValid(url))
        {
            throw new BadRequestException("SCM link URL must be an absolute http(s) URL.");
        }
    }
}
