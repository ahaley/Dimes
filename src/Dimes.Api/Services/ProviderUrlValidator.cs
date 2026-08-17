using System.Net;
using System.Net.Sockets;
using Dimes.Domain;

namespace Dimes.Api.Services;

/// <summary>Validates an LLM provider <c>BaseUrl</c> before it is persisted and again before each
/// outbound call, to prevent SSRF and credential exfiltration.
///
/// A stored BaseUrl is later used verbatim as an outbound request target whenever an (authenticated,
/// recommend-only) agent comment is posted, and the response body is stored back as a <c>Comment</c>. An
/// unvalidated BaseUrl therefore turns the server into an SSRF-and-exfiltration proxy — and worse, the
/// adapter attaches the config's resolved credential to that request, so an attacker-chosen host collects
/// the key.
///
/// The policy is **per provider type**, because the types have genuinely different needs:
///
/// <list type="bullet">
/// <item><see cref="LlmProviderType.OpenAICompatible"/> must stay permissive — its whole purpose is local
/// model runners (Ollama / vLLM / LM Studio) on localhost or the LAN, the data-stays-local path the spec
/// preserves. Only the scheme and link-local checks apply.</item>
/// <item>The vendor-hosted types (<see cref="LlmProviderType.Anthropic"/>,
/// <see cref="LlmProviderType.Gemini"/>, <see cref="LlmProviderType.GeminiVertex"/>) additionally require
/// the host to sit under that vendor's domain. An override is still useful there (a regional Vertex host,
/// a Private Service Connect endpoint, a corporate gateway on the vendor domain) but pointing a
/// key-bearing vendor request at an arbitrary host is never legitimate, so it is refused.</item>
/// </list>
///
/// Before this split, any type could be pointed anywhere; the vendor allowlist is what stops a
/// provider-config admin from turning a stored Anthropic or Gemini key into an outbound credential leak.</summary>
public static class ProviderUrlValidator
{
    /// <summary>Permitted host suffixes per type. Absent = no host restriction beyond the shared checks.
    /// Matching is on a dotted suffix (or the bare domain), so <c>evilgoogleapis.com</c> does not match.</summary>
    private static readonly Dictionary<LlmProviderType, string[]> AllowedHostSuffixes = new()
    {
        [LlmProviderType.Anthropic] = ["anthropic.com"],
        [LlmProviderType.Gemini] = ["googleapis.com"],
        [LlmProviderType.GeminiVertex] = ["googleapis.com"],
    };

    public static async Task ValidateAsync(
        LlmProviderType type, string? baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return; // No override — the adapter falls back to its safe vendor default.
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BadRequestException("Provider base URL must be an absolute http(s) URL.");
        }

        if (AllowedHostSuffixes.TryGetValue(type, out var suffixes) && !IsUnder(uri.IdnHost, suffixes))
        {
            throw new BadRequestException(
                $"A base URL override for a {type} provider must be on {string.Join(" or ", suffixes)}. " +
                "To reach a different host, use an OpenAI-compatible provider instead.");
        }

        // Check the address(es) the request would actually reach. An IP literal resolves to itself; a
        // hostname (e.g. metadata.google.internal) is resolved so a DNS alias to a metadata IP is caught.
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.IdnHost, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, ct);
            }
            catch (SocketException)
            {
                // Unresolvable when saved (typo, or DNS not reachable here). Don't hard-fail the config
                // on a transient lookup — the scheme check above still applies and an unreachable host
                // simply fails at request time.
                return;
            }
        }

        if (addresses.Any(IsLinkLocal))
        {
            throw new BadRequestException(
                "Provider base URL resolves to a link-local address (e.g. a cloud metadata endpoint), which is not allowed.");
        }
    }

    /// <summary>Suffix match on label boundaries: the host must equal the domain or end with a dot plus
    /// the domain. A plain <c>EndsWith</c> would accept <c>notgoogleapis.com</c>.</summary>
    private static bool IsUnder(string host, string[] suffixes) =>
        suffixes.Any(suffix =>
            host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));

    private static bool IsLinkLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal;
        }

        // IPv4 169.254.0.0/16.
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
