using System.Net;
using System.Net.Sockets;

namespace Dimes.Api.Services;

/// <summary>Validates an LLM provider <c>BaseUrl</c> before it is persisted, to prevent SSRF.
///
/// A stored BaseUrl is later used verbatim as an outbound request target whenever an (authenticated,
/// recommend-only) agent comment is posted, and the response body is stored back as a <c>Comment</c>.
/// An unvalidated BaseUrl therefore turns the server into an SSRF-and-exfiltration proxy. We require an
/// absolute http(s) URL and reject link-local addresses, which cover the cloud instance-metadata
/// endpoints (IPv4 169.254.0.0/16 — e.g. 169.254.169.254 / ECS 169.254.170.2 — and IPv6 fe80::/10).
/// Those are never a legitimate provider and are the classic exfiltration target.
///
/// Loopback and private-LAN addresses are intentionally NOT blocked: the OpenAI-compatible adapter's
/// whole purpose is local model runners (Ollama / vLLM / LM Studio) on localhost or the LAN.</summary>
public static class ProviderUrlValidator
{
    public static async Task ValidateAsync(string? baseUrl, CancellationToken ct = default)
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
