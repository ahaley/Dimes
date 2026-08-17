using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dimes.Domain;
using Dimes.Domain.Providers;
using Google.Apis.Auth.OAuth2;

namespace Dimes.Infrastructure.Providers;

/// <summary>Gemini adapter over Vertex AI. Same <c>generateContent</c> body as
/// <see cref="GeminiLlmProvider"/>, but a per-region host, a project-scoped URL, and OAuth2 bearer auth
/// instead of an API key.
///
/// This is a separate provider type rather than a flag on the Gemini one because the *auth model* differs,
/// which is the bar this codebase sets for a native adapter. What it buys operators: a regional endpoint
/// (data residency), VPC Service Controls, CMEK, and GCP billing/quota instead of a distributed API key.
///
/// Preferred configuration is Application Default Credentials — on GKE / Cloud Run with Workload Identity
/// there is then no stored secret at all, which is a stronger position than any secret reference. The
/// credentials-JSON path exists for hosts without ADC and reuses the caching approach already proven in
/// <see cref="GoogleChatNotificationProvider"/>.</summary>
public sealed class GeminiVertexLlmProvider(HttpClient http) : ILlmProvider
{
    private const string CloudPlatformScope = "https://www.googleapis.com/auth/cloud-platform";
    private const string ApiVersion = "v1";

    /// <summary>Cache key for the Application Default Credentials entry; ADC has no secret to fingerprint.</summary>
    private const string AdcCacheKey = "\0adc";

    /// <summary>Upper bound before a credential value is even considered a path. Generous next to a real
    /// path and far below a credential document, so it only rejects things that were never paths.</summary>
    private const int MaxCredentialPathLength = 4096;

    // Google's ITokenAccess caches and refreshes the underlying access token, so caching the scoped
    // credential is enough to avoid re-parsing/re-minting per call. The value is a Task so the async ADC
    // lookup can be memoized without ever blocking on it (no sync-over-async).
    private static readonly ConcurrentDictionary<string, Task<ITokenAccess>> CredentialCache = new();

    public LlmProviderType Type => LlmProviderType.GeminiVertex;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
    {
        LlmProviderSettings settings = connection.Settings ?? new LlmProviderSettings();
        string project = Require(settings.GcpProject, "GCP project");
        string location = Require(settings.GcpLocation, "GCP location");

        string baseUrl = (connection.BaseUrl ?? DefaultHost(location)).TrimEnd('/');
        string url =
            $"{baseUrl}/{ApiVersion}/projects/{Uri.EscapeDataString(project)}" +
            $"/locations/{Uri.EscapeDataString(location)}/publishers/google/models" +
            $"/{Uri.EscapeDataString(connection.Model)}:generateContent";

        string accessToken = await GetAccessTokenAsync(connection, settings, ct);

        using HttpRequestMessage message = new(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(GeminiGenerateContent.BuildRequest(request)),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await http.SendAsync(message, ct);
        await LlmHttp.EnsureSuccessAsync(response, "Vertex AI", ct);

        GeminiGenerateContent.GeminiResponse? body =
            await response.Content.ReadFromJsonAsync<GeminiGenerateContent.GeminiResponse>(ct);
        return new LlmCompletionResult(GeminiGenerateContent.ExtractText(body));
    }

    /// <summary>Vertex is addressed by regional host; the multi-region pool uses the unprefixed host with
    /// <c>locations/global</c> in the path.</summary>
    private static string DefaultHost(string location) =>
        location.Equals("global", StringComparison.OrdinalIgnoreCase)
            ? "https://aiplatform.googleapis.com"
            : $"https://{location}-aiplatform.googleapis.com";

    private static async Task<string> GetAccessTokenAsync(
        LlmConnection connection, LlmProviderSettings settings, CancellationToken ct)
    {
        // ADC on a GCE-family host mints its token from the instance metadata server (a link-local
        // address). That is unrelated to the ProviderUrlValidator link-local ban, which guards the
        // operator-supplied BaseUrl — here the address is chosen by Google's library, not by input.
        (string Key, Func<Task<ITokenAccess>> Factory) entry;
        if (settings.UseApplicationDefaultCredentials)
        {
            entry = (AdcCacheKey, ScopedApplicationDefaultAsync);
        }
        else
        {
            // Resolve to the JSON before fingerprinting, so the cache key is the key material rather than
            // the path that pointed at it. That costs a small local file read per call and buys correct
            // rotation: replacing the file in place changes the fingerprint, so the next call mints from
            // the new key instead of serving a credential cached under an unchanged path string.
            string json = ReadCredentialsJson(RequireCredentials(connection.ApiKey));
            entry = (Fingerprint(json), () => Task.FromResult(FromCredentialsJson(json)));
        }

        ITokenAccess credential = await GetCredentialAsync(entry.Key, entry.Factory);
        return await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    /// <summary>Memoize the scoped credential, evicting a failed attempt.
    ///
    /// Caching the Task is what lets the async ADC lookup be shared without blocking on it, but a cached
    /// *faulted* Task would be permanent: one transient metadata-server hiccup would fail every later call
    /// with the same stale exception until the process restarted. So a failure is removed from the cache
    /// and the next call retries.</summary>
    private static async Task<ITokenAccess> GetCredentialAsync(string key, Func<Task<ITokenAccess>> factory)
    {
        Task<ITokenAccess> task = CredentialCache.GetOrAdd(key, _ => factory());
        try
        {
            return await task;
        }
        catch
        {
            CredentialCache.TryRemove(new KeyValuePair<string, Task<ITokenAccess>>(key, task));
            throw;
        }
    }

    /// <summary>Resolve the configured credential to service-account JSON, reading it from disk when the
    /// reference resolved to a <em>path</em> rather than the document itself.
    ///
    /// Operators hold a service-account key as a file, so binding the reference to its path is the natural
    /// thing to do — and it previously failed with a "that's a path, not JSON" error that made the operator
    /// go set up a second setting. Now either form works, whichever route bound the name: a plain
    /// <c>&lt;name&gt;</c> environment variable, <c>Secrets:&lt;name&gt;</c>, or the explicit file routes.
    /// Pointing the reference straight at <c>GOOGLE_APPLICATION_CREDENTIALS</c> therefore also works.
    ///
    /// **Scoped to this adapter deliberately.** A Vertex credential is a JSON *document*, so a value that
    /// isn't JSON can only be a path — there is no ambiguity to get wrong. The same guess would not be safe
    /// for a bearer-token provider: the reference *name* comes from the provider form, so a project
    /// Maintainer can name any environment variable, and a token-shaped credential is sent verbatim to the
    /// configured endpoint — which for an OpenAI-compatible provider may be any host. Here the file contents
    /// never leave the process: they are parsed locally and exchanged for a Google-signed token usable only
    /// against <c>*.googleapis.com</c>, which is the same trust the Application Default Credentials option
    /// already grants. Bearer-token types keep requiring the explicit
    /// <c>SecretFiles:&lt;name&gt;</c> / <c>&lt;name&gt;_FILE</c> routes.</summary>
    private static string ReadCredentialsJson(string credential)
    {
        if (LooksLikeJson(credential))
        {
            return credential;
        }

        // Require a rooted path before treating the value as one — and before echoing it in any error.
        // A misconfiguration that put an API key here would otherwise be written into the error message and
        // from there into logs; keys are never rooted paths, so this both narrows the guess and avoids
        // leaking the secret. A relative path is refused on purpose: it would resolve against the host
        // process's working directory, which is not something an operator can reason about.
        string path = credential.Trim();
        if (path.Length > MaxCredentialPathLength
            || !Path.IsPathRooted(path)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidOperationException(
                "The Vertex AI credential is neither service-account JSON nor an absolute file path. The " +
                "reference must resolve to the credentials JSON itself, or to the absolute path of the key " +
                "file — or enable Application Default Credentials instead.");
        }

        try
        {
            return File.ReadAllText(path).TrimEnd('\r', '\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The Vertex AI credential points at '{path}', which could not be read: {ex.Message}", ex);
        }
    }

    private static bool LooksLikeJson(string value) => value.TrimStart().StartsWith('{');

    /// <summary>Build a scoped credential from a service-account credentials JSON.</summary>
    private static ITokenAccess FromCredentialsJson(string credentials)
    {
        try
        {
            return GoogleCredential.FromJson(credentials).CreateScoped(CloudPlatformScope);
        }
        catch (Exception ex)
        {
            // Broad on purpose: the JSON parser here belongs to a transitive dependency of
            // Google.Apis.Auth that this project does not reference, so its exception type cannot be
            // named. Nothing else in this call can fail, and the cause is preserved.
            throw new InvalidOperationException(
                "The Vertex AI credential is not a valid Google service-account credentials JSON " +
                $"({ex.Message}). Re-download the key from the GCP console, or enable Application Default " +
                "Credentials instead.", ex);
        }
    }

    private static async Task<ITokenAccess> ScopedApplicationDefaultAsync()
    {
        GoogleCredential credential = await GoogleCredential.GetApplicationDefaultAsync();
        // A metadata-server or user credential is already scoped; only a service-account key needs this.
        return credential.IsCreateScopedRequired ? credential.CreateScoped(CloudPlatformScope) : credential;
    }

    /// <summary>A stable cache key for a credentials JSON that never holds the secret in plaintext.</summary>
    private static string Fingerprint(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Require(string? value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The Vertex AI provider has no {what} configured.")
            : value;

    private static string RequireCredentials(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey)
            ? throw new InvalidOperationException(
                "The Vertex AI provider resolved no service-account credentials JSON. Check the secret " +
                "reference, or enable Application Default Credentials instead.")
            : apiKey;
}
