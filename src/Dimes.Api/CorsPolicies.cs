namespace Dimes.Api;

/// <summary>Names for the CORS policies registered in <c>Program.cs</c>, shared so the policy
/// definition and the <c>[EnableCors]</c> attribute can't drift apart.</summary>
public static class CorsPolicies
{
    /// <summary>Cross-origin policy for the anonymous observation ingest endpoint, so the capture SDK
    /// can post from a host app on a different origin. Origins come from <c>Cors:AllowedOrigins</c>;
    /// credentials are never allowed because the endpoint reads no cookies.</summary>
    public const string Ingest = "ingest-cors";
}
