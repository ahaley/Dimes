using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dimes.Domain;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>GitHub adapter — read-only context pull for a PR or issue URL via the REST API.
/// PRs are issues for title/body/state purposes, so one endpoint serves both.</summary>
public sealed partial class GitHubScmProvider(HttpClient http) : IScmProvider
{
    private const string ApiBase = "https://api.github.com";

    public ScmProviderType Type => ScmProviderType.GitHub;

    public async Task<ScmContext?> FetchContextAsync(string url, string? token, CancellationToken ct = default)
    {
        var match = PrOrIssueUrl().Match(url ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var (owner, repo, number) = (match.Groups["owner"].Value, match.Groups["repo"].Value, match.Groups["number"].Value);

        using var message = new HttpRequestMessage(
            HttpMethod.Get, $"{ApiBase}/repos/{owner}/{repo}/issues/{number}");
        message.Headers.UserAgent.Add(new ProductInfoHeaderValue("Dimes", "0.1"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var issue = await response.Content.ReadFromJsonAsync<GitHubIssue>(ct);
        if (issue is null)
        {
            return null;
        }

        var raw = $"{issue.Title}\n\n{issue.Body}".Trim();
        return new ScmContext(issue.Title, issue.Body, issue.State, raw);
    }

    [GeneratedRegex(@"github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/(?:pull|issues)/(?<number>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrOrIssueUrl();

    private sealed record GitHubIssue(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("state")] string? State);
}
