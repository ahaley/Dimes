using System.Security.Claims;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Dimes.Api.Auth;

/// <summary>Wires the BFF auth pipeline: a cookie session is the end state for BOTH modes. Cookie is
/// always the default + challenge scheme (so XHRs get 401s, never an auto-redirect to the IdP). In
/// Oidc mode the OpenID Connect handler is added and invoked only explicitly via /api/auth/challenge,
/// signing the resulting principal into the same cookie. The OIDC client secret is resolved through
/// <see cref="ISecretResolver"/> so it stays out of config/DB.</summary>
public static class AuthExtensions
{
    public static IServiceCollection AddDimesAuthentication(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(section);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, CurrentActor>();
        services.AddSingleton<IPasswordHasher<Actor>, PasswordHasher<Actor>>();
        services.AddScoped<AuthBootstrapper>();

        var auth = services.AddAuthentication(o =>
        {
            o.DefaultScheme = AuthSchemes.Cookie;
            o.DefaultAuthenticateScheme = AuthSchemes.Cookie;
            o.DefaultSignInScheme = AuthSchemes.Cookie;
            o.DefaultChallengeScheme = AuthSchemes.Cookie;
        });

        auth.AddCookie(AuthSchemes.Cookie, c =>
        {
            c.Cookie.Name = "dimes.session";
            c.Cookie.HttpOnly = true;
            c.Cookie.SameSite = SameSiteMode.Lax;
            c.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            c.SlidingExpiration = true;
            c.ExpireTimeSpan = TimeSpan.FromDays(7);
            // This is an API, not a server-rendered app: return status codes instead of redirecting.
            c.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            c.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            // Reconcile the session against the DB on each request so site-admin grants/revocations and
            // archival take effect immediately, without forcing a sign-out/sign-in.
            c.Events.OnValidatePrincipal = OnValidatePrincipalAsync;
        });

        if (options.Mode == AuthMode.Oidc)
        {
            // Fail fast at startup with a clear message. The OpenID Connect handler validates its own
            // options lazily on the FIRST request — so a missing Authority would otherwise surface as a
            // 500 on every endpoint (including /health) rather than a boot error.
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Oidc.Authority)) missing.Add($"{AuthOptions.SectionName}:Oidc:Authority");
            if (string.IsNullOrWhiteSpace(options.Oidc.ClientId)) missing.Add($"{AuthOptions.SectionName}:Oidc:ClientId");
            if (string.IsNullOrWhiteSpace(options.Oidc.ClientSecretRef)) missing.Add($"{AuthOptions.SectionName}:Oidc:ClientSecretRef");
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{AuthOptions.SectionName}:Mode is 'Oidc' but required OIDC settings are missing: " +
                    $"{string.Join(", ", missing)}. Set them (Authority is the realm issuer URL, e.g. " +
                    $"https://keycloak.example.com/realms/dimes), or set {AuthOptions.SectionName}:Mode to 'Local'.");
            }

            auth.AddOpenIdConnect(AuthSchemes.Oidc, o =>
            {
                o.Authority = options.Oidc.Authority;
                o.ClientId = options.Oidc.ClientId;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.CallbackPath = options.Oidc.CallbackPath;
                o.SignInScheme = AuthSchemes.Cookie;
                o.SaveTokens = false; // BFF: no tokens handed to the browser.
                o.GetClaimsFromUserInfoEndpoint = true;
                o.MapInboundClaims = false;
                o.Scope.Clear();
                foreach (var scope in options.Oidc.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    o.Scope.Add(scope);
                }
                o.TokenValidationParameters.NameClaimType = "name";
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.Events = new OpenIdConnectEvents { OnTokenValidated = OnTokenValidatedAsync };
            });

            // Resolve the client secret from the secret store (config Secrets:{ref} or env var).
            services.AddOptions<OpenIdConnectOptions>(AuthSchemes.Oidc)
                .Configure<ISecretResolver>((o, secrets) => o.ClientSecret = secrets.Resolve(options.Oidc.ClientSecretRef));
        }

        return services;
    }

    /// <summary>Add the auth-related authorization policies. The fallback policy (require an
    /// authenticated user on everything not marked [AllowAnonymous]) is set in Program.cs.</summary>
    public static AuthorizationOptions AddDimesAuthorizationPolicies(this AuthorizationOptions o)
    {
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        o.AddPolicy(DimesClaims.SiteAdminPolicy, p => p.RequireClaim(DimesClaims.SiteAdmin, "true"));
        return o;
    }

    /// <summary>Runs on every authenticated request: re-reads the actor's site-admin/archived state
    /// from the store and reconciles the <see cref="DimesClaims.SiteAdmin"/> claim that was baked into
    /// the cookie at sign-in. Without this, a freshly-elevated admin keeps a pre-elevation cookie (no
    /// claim) and is forbidden from site-admin actions until they sign out and back in — and likewise a
    /// revoked admin or archived user would retain access for the life of the cookie (up to a week).
    /// The cookie remains the per-request cache; this is a single indexed PK lookup to keep it honest.</summary>
    private static async Task OnValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal?.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return;
        }
        if (!Guid.TryParse(ctx.Principal.FindFirst(DimesClaims.ActorId)?.Value, out var actorId))
        {
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<DimesDbContext>();
        var actor = await db.Actors
            .AsNoTracking()
            .Where(a => a.Id == actorId)
            .Select(a => new { a.IsSiteAdmin, a.IsArchived })
            .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

        // Actor deleted or archived since the session was issued: terminate the session.
        if (actor is null || actor.IsArchived)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(AuthSchemes.Cookie);
            return;
        }

        var hasClaim = ctx.Principal.HasClaim(DimesClaims.SiteAdmin, "true");
        if (actor.IsSiteAdmin && !hasClaim)
        {
            identity.AddClaim(new Claim(DimesClaims.SiteAdmin, "true"));
            ctx.ShouldRenew = true; // persist the reconciled claim back to the cookie
        }
        else if (!actor.IsSiteAdmin && hasClaim)
        {
            foreach (var stale in identity.FindAll(DimesClaims.SiteAdmin).ToList())
            {
                identity.RemoveClaim(stale);
            }
            ctx.ShouldRenew = true;
        }
    }

    /// <summary>JIT-provision the external identity into an Actor and stamp the Dimes session claims
    /// onto the cookie principal.</summary>
    private static async Task OnTokenValidatedAsync(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal;
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            ctx.Fail("No identity from the identity provider.");
            return;
        }

        var email = principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            ctx.Fail("The identity provider did not return an email claim.");
            return;
        }

        // We provision/match an actor by email (including the bootstrapped site admin), so an IdP that
        // hands out accounts with arbitrary unverified emails would let an attacker claim someone else's
        // identity — including the configured admin email. Reject when the IdP explicitly marks the
        // email unverified. (Absent claim is tolerated: not every IdP emits email_verified.)
        if (string.Equals(principal.FindFirst("email_verified")?.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Fail("The identity provider reports this email address as unverified.");
            return;
        }
        var name = principal.FindFirst("name")?.Value ?? principal.Identity?.Name ?? email;

        var db = ctx.HttpContext.RequestServices.GetRequiredService<DimesDbContext>();
        var actor = await JitProvisioning.ProvisionAsync(db, email, name, ctx.HttpContext.RequestAborted);
        if (actor.IsArchived)
        {
            ctx.Fail("This account has been archived.");
            return;
        }

        identity.AddClaim(new Claim(DimesClaims.ActorId, actor.Id.ToString()));
        if (actor.IsSiteAdmin)
        {
            identity.AddClaim(new Claim(DimesClaims.SiteAdmin, "true"));
        }
    }
}
