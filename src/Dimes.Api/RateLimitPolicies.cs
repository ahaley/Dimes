namespace Dimes.Api;

/// <summary>Names for the rate-limiting policies registered in <c>Program.cs</c>, shared so the policy
/// definition and the <c>[EnableRateLimiting]</c> attribute can't drift apart.</summary>
public static class RateLimitPolicies
{
    /// <summary>Per-source fixed window on the anonymous observation capture endpoint.</summary>
    public const string Ingest = "ingest";

    /// <summary>Per-client-IP fixed window on the anonymous local-login endpoint, to blunt online
    /// password brute-forcing and credential stuffing.</summary>
    public const string Login = "login";
}
