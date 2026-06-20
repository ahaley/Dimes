namespace Dimes.Api;

/// <summary>Names for the rate-limiting policies registered in <c>Program.cs</c>, shared so the policy
/// definition and the <c>[EnableRateLimiting]</c> attribute can't drift apart.</summary>
public static class RateLimitPolicies
{
    /// <summary>Per-source fixed window on the anonymous observation capture endpoint.</summary>
    public const string Ingest = "ingest";
}
