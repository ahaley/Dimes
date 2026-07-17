using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Dimes.Domain;
using Dimes.Domain.Providers;
using Google.Apis.Auth.OAuth2;

namespace Dimes.Infrastructure.Providers;

/// <summary>Google Chat adapter. Posts a bot message to a configured space via the Chat REST API,
/// authenticating with a service account whose credentials JSON is the channel's referenced secret.
///
/// Unlike the LLM adapters there is no user-supplied base URL — the host is fixed (<c>chat.googleapis.com</c>),
/// so no <c>ProviderUrlValidator</c>/SSRF guard is needed here. A future Webhook adapter that DOES take a
/// user-supplied URL MUST validate it at both save and send time (see <c>ProviderUrlValidator</c>).</summary>
public sealed class GoogleChatNotificationProvider(HttpClient http) : INotificationChannelProvider
{
    private const string ChatBaseUrl = "https://chat.googleapis.com/v1";
    private const string ChatScope = "https://www.googleapis.com/auth/chat.bot";

    // Google's ITokenAccess caches and auto-refreshes the underlying access token, so caching the scoped
    // credential per distinct credentials-JSON is enough to avoid re-parsing/re-minting on every send.
    private static readonly ConcurrentDictionary<string, ITokenAccess> CredentialCache = new();

    public NotificationChannelType Type => NotificationChannelType.GoogleChat;

    public async Task SendAsync(
        NotificationMessage message, NotificationConnection connection, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connection.Secret))
        {
            throw new InvalidOperationException(
                "Google Chat channel has no credentials secret configured (set its secret reference).");
        }
        if (string.IsNullOrWhiteSpace(connection.Target))
        {
            throw new InvalidOperationException("Google Chat channel has no target space configured.");
        }

        var accessToken = await GetAccessTokenAsync(connection.Secret, ct);

        // Target is a space resource name like "spaces/AAAA"; normalize a bare id just in case.
        var space = connection.Target.Trim();
        if (!space.StartsWith("spaces/", StringComparison.OrdinalIgnoreCase))
        {
            space = $"spaces/{space}";
        }

        var text = string.IsNullOrWhiteSpace(message.Title)
            ? message.Body
            : $"*{message.Title}*\n\n{message.Body}";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ChatBaseUrl}/{space}/messages")
        {
            Content = JsonContent.Create(new ChatMessageRequest(text)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Surface the Chat API's error body so the drain worker's LastError is actionable.
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Google Chat send failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Trim(body)}");
        }
    }

    private static async Task<string> GetAccessTokenAsync(string credentialsJson, CancellationToken ct)
    {
        var key = Fingerprint(credentialsJson);
        var credential = CredentialCache.GetOrAdd(key, _ =>
            GoogleCredential.FromJson(credentialsJson).CreateScoped(ChatScope));
        return await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    /// <summary>A stable cache key for a credentials JSON that never holds the secret in plaintext.</summary>
    private static string Fingerprint(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Trim(string s) => s.Length <= 500 ? s : s[..500];

    private sealed record ChatMessageRequest(
        [property: JsonPropertyName("text")] string Text);
}
