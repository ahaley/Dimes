namespace Dimes.Api.Auth;

/// <summary>How a deployment authenticates users. Chosen via config (<c>Auth:Mode</c>); changing it
/// requires an API restart since the auth pipeline is wired at startup.</summary>
public enum AuthMode
{
    /// <summary>Local email + password sessions managed by Dimes.</summary>
    Local,

    /// <summary>OpenID Connect (e.g. Keycloak) via the server-side authorization-code flow.</summary>
    Oidc,
}

/// <summary>Bound from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Local;
    public OidcOptions Oidc { get; set; } = new();
    public SiteAdminOptions SiteAdmin { get; set; } = new();
}

public sealed class OidcOptions
{
    public string? Authority { get; set; }
    public string? ClientId { get; set; }

    /// <summary>Reference resolved via <see cref="Dimes.Domain.Providers.ISecretResolver"/> — the
    /// client secret itself is never stored in config/DB, only its reference name.</summary>
    public string? ClientSecretRef { get; set; }

    public string CallbackPath { get; set; } = "/signin-oidc";
    public string Scopes { get; set; } = "openid email profile";
}

public sealed class SiteAdminOptions
{
    /// <summary>Email of the actor seeded/promoted to site admin on startup.</summary>
    public string? Email { get; set; }

    /// <summary>Local mode only: initial password used to create the admin's credential if missing.</summary>
    public string? InitialPassword { get; set; }
}

/// <summary>Authentication scheme names.</summary>
public static class AuthSchemes
{
    public const string Cookie = "DimesCookie";
    public const string Oidc = "DimesOidc";
}

/// <summary>Custom claim types stamped onto the session principal.</summary>
public static class DimesClaims
{
    public const string ActorId = "dimes:actor_id";
    public const string SiteAdmin = "dimes:site_admin";

    /// <summary>Authorization policy requiring an app-level site administrator.</summary>
    public const string SiteAdminPolicy = "SiteAdmin";
}
