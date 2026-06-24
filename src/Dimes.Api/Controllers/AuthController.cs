using System.Security.Claims;
using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dimes.Api.Controllers;

/// <summary>Session endpoints for both auth modes. Local mode uses <c>/login</c>; OIDC mode uses
/// <c>/challenge</c> (a full-page navigation that runs the Keycloak code flow). Both end in a cookie
/// session, so the rest of the API is identical regardless of mode.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    DimesDbContext db,
    ICurrentActor currentActor,
    IPasswordHasher<Actor> hasher,
    IOptions<AuthOptions> options) : ControllerBase
{
    /// <summary>A throwaway hash verified on the no-such-user / no-credential path so it costs the same
    /// PBKDF2 work as a real login. Without this, the early return for an unknown email is measurably
    /// faster than a wrong password, letting an attacker enumerate valid accounts by latency.</summary>
    private static readonly string DummyPasswordHash =
        new PasswordHasher<Actor>().HashPassword(new Actor { DisplayName = "" }, "timing-equalization-placeholder");

    /// <summary>The deployment's auth mode, so the SPA can render the correct login UI. Anonymous.</summary>
    [AllowAnonymous]
    [HttpGet("config")]
    public ActionResult<AuthConfigDto> Config() => Ok(new AuthConfigDto(options.Value.Mode.ToString()));

    /// <summary>The authenticated user, or 401 (enforced by the fallback policy).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        var actor = await currentActor.GetAsync(ct);
        return Ok(ToMe(actor));
    }

    /// <summary>Local-mode email + password login. Returns 404 in OIDC mode.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpPost("login")]
    public async Task<ActionResult<MeDto>> Login(LoginRequest req, CancellationToken ct)
    {
        if (options.Value.Mode != AuthMode.Local)
        {
            return NotFound();
        }

        var email = req.Email?.Trim().ToLowerInvariant();
        var actor = string.IsNullOrWhiteSpace(email)
            ? null
            : await db.Actors.FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == email, ct);
        var credential = actor is null
            ? null
            : await db.LocalCredentials.FirstOrDefaultAsync(c => c.ActorId == actor.Id, ct);

        if (actor is null || credential is null || actor.IsArchived)
        {
            // Verify against a dummy hash so an unknown/archived account costs the same as a real one —
            // closes the account-enumeration timing oracle. The result is discarded; this still 401s.
            hasher.VerifyHashedPassword(actor ?? new Actor { DisplayName = "" }, credential?.PasswordHash ?? DummyPasswordHash, req.Password ?? string.Empty);
            throw new UnauthorizedException("Invalid email or password.");
        }

        var result = hasher.VerifyHashedPassword(actor, credential.PasswordHash, req.Password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedException("Invalid email or password.");
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = hasher.HashPassword(actor, req.Password!);
            credential.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        await HttpContext.SignInAsync(
            AuthSchemes.Cookie, BuildPrincipal(actor), new AuthenticationProperties { IsPersistent = true });
        return Ok(ToMe(actor));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Cookie);
        return NoContent();
    }

    /// <summary>OIDC-mode entry point — a real browser navigation that challenges Keycloak and, on
    /// return, lands back at <paramref name="returnUrl"/> with a cookie. Returns 404 in local mode.</summary>
    [AllowAnonymous]
    [HttpGet("challenge")]
    public IActionResult ChallengeOidc([FromQuery] string? returnUrl)
    {
        if (options.Value.Mode != AuthMode.Oidc)
        {
            return NotFound();
        }

        var redirect = string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/" : returnUrl;
        return Challenge(new AuthenticationProperties { RedirectUri = redirect }, AuthSchemes.Oidc);
    }

    private static MeDto ToMe(Actor actor) => new(actor.Id, actor.DisplayName, actor.Email, actor.IsSiteAdmin);

    private static ClaimsPrincipal BuildPrincipal(Actor actor)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actor.Id.ToString()),
            new(ClaimTypes.Name, actor.DisplayName),
            new(DimesClaims.ActorId, actor.Id.ToString()),
        };
        if (!string.IsNullOrEmpty(actor.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, actor.Email));
        }
        if (actor.IsSiteAdmin)
        {
            claims.Add(new Claim(DimesClaims.SiteAdmin, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Cookie));
    }
}
