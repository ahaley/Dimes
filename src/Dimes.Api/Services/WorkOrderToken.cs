using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Dimes.Api.Services;

/// <summary>Mints and hashes the per-export work-order capability token. The raw token is emitted once,
/// into the exported markdown, and never stored — only its hash is, so a database read can't be replayed
/// against the ingest endpoint.</summary>
public static class WorkOrderToken
{
    /// <summary>256 bits of CSPRNG entropy, Base64Url so it survives a URL path segment unescaped.</summary>
    public static string Mint() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256, hex. No salt or work factor by design: unlike a password this is full-entropy
    /// random, so there is nothing to brute-force and a slow KDF would only tax the ingest path.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
