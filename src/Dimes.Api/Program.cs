using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Dimes.Api;
using Dimes.Api.Auth;
using Dimes.Api.Realtime;
using Dimes.Api.Services;
using Dimes.Infrastructure;
using Dimes.Infrastructure.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Resolve the database connection and provider. An explicit ConnectionStrings:Dimes (env or config)
// wins — a Postgres URL/keyword string selects Postgres (managed PG hands out a postgresql:// URI,
// which we normalize to Npgsql's keyword form). Otherwise default to an absolute, cwd-independent
// SQLite file under <contentRoot>/data so projects persist across runs wherever the app is launched.
var connectionString = builder.Configuration.GetConnectionString("Dimes");
var dbProvider = DatabaseConnection.Detect(connectionString);
if (dbProvider == DatabaseProvider.Postgres)
{
    connectionString = DatabaseConnection.NormalizePostgres(connectionString!);
}
else if (string.IsNullOrWhiteSpace(connectionString))
{
    var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Directory.CreateDirectory(dataDir);
    connectionString = $"Data Source={Path.Combine(dataDir, "dimes.db")}";
}

builder.Services.AddDimesPersistence(connectionString, dbProvider);
builder.Services.AddDimesProviders();

// Persist the Data Protection key ring in the database (a DataProtectionKeys table managed via the
// DbContext). The BFF session cookie is encrypted with these keys; without durable storage the keys are
// ephemeral and every restart/redeploy silently invalidates all sessions. A fixed application name keeps
// the ring stable even if the deployment path changes between releases.
builder.Services.AddDataProtection()
    .SetApplicationName("Dimes")
    .PersistKeysToDbContext<DimesDbContext>();

// Authentication: cookie session backed by local login or Keycloak OIDC, chosen via Auth:Mode.
builder.Services.AddDimesAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(o => o.AddDimesAuthorizationPolicies());

// When deployed behind a TLS-terminating reverse proxy, honor X-Forwarded-Proto/Host so the OIDC
// redirect_uri and the Secure-cookie decision use the real external scheme/host (the proxy forwards
// plain HTTP to the API). Off by default — enable ONLY when the API is reachable *exclusively*
// through a trusted proxy, since trusting these headers from a directly-reachable API lets a client
// spoof its apparent scheme/host.
var useForwardedHeaders = builder.Configuration.GetValue<bool>("Proxy:UseForwardedHeaders");
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        // The proxy's address is rarely known ahead of time in self-host setups; trust it because the
        // API is only routed to via the proxy. Populate these if the API is also directly reachable.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// Application services (capture → inbox → promote → lifecycle, commentary, SCM context).
builder.Services.AddScoped<MembershipResolver>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ObservationService>();
builder.Services.AddScoped<ChangeRequestService>();
builder.Services.AddScoped<CommentaryService>();
builder.Services.AddScoped<CaptureAssistService>();
builder.Services.AddScoped<AssistConversationService>();
builder.Services.AddScoped<ScmService>();
builder.Services.AddScoped<SiteAdminService>();
builder.Services.AddScoped<SiteSettingsService>();
builder.Services.AddScoped<IdentifierBootstrapper>();
builder.Services.AddScoped<SystemInstructionBootstrapper>();

// Realtime board updates (SignalR).
builder.Services.AddSignalR();
builder.Services.AddSingleton<IBoardNotifier, SignalRBoardNotifier>();

// Throttle the anonymous capture endpoint. It's [AllowAnonymous] (host apps have no Dimes session),
// so the only control is the unguessable source id — a leaked id could otherwise flood unbounded
// observations. Partition a fixed window per source id (falling back to client IP), and reject excess
// with 429 rather than queueing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Ingest, httpContext =>
    {
        var key = httpContext.Request.RouteValues.TryGetValue("sourceId", out var sourceId) && sourceId is not null
            ? sourceId.ToString()!
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Throttle local login per client IP. PBKDF2 verification slows offline cracking but does nothing
    // against unlimited online guessing, and the login endpoint is [AllowAnonymous] with no account
    // lockout — so cap attempts per source IP and 429 the excess. Generous enough for fat-fingered
    // humans, tight enough to make brute force / credential stuffing impractical.
    options.AddPolicy(RateLimitPolicies.Login, httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

// Cross-origin access for the capture SDK. Only the anonymous observation ingest endpoint opts in
// (via [EnableCors(CorsPolicies.Ingest)]); every other endpoint stays same-origin, which the BFF
// session cookie and the OIDC redirect flow rely on. Origins are an operator allowlist of host apps
// that embed the SDK from another domain; credentials are never allowed because the endpoint reads no
// cookies (the unguessable source id is the only capability). Empty/unset => no cross-origin access.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(CorsPolicies.Ingest, policy =>
    policy.WithOrigins(corsOrigins)
        .WithMethods("POST")
        .WithHeaders("Content-Type")
        .SetPreflightMaxAge(TimeSpan.FromHours(1))));

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

// Must run before anything that reads the request scheme/host (HTTPS redirect, OIDC, cookies).
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();

// Apply pending migrations on startup, then seed the bootstrap site admin from config, so a fresh
// self-host install needs no manual DB step and has a way in.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DimesDbContext>();
    db.Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<AuthBootstrapper>().SeedAsync();
    // Backfill display-key/number on any pre-feature projects and changes (idempotent).
    await scope.ServiceProvider.GetRequiredService<IdentifierBootstrapper>().BackfillAsync();
    // Seed each project's editable system instructions from the built-in defaults (idempotent).
    await scope.ServiceProvider.GetRequiredService<SystemInstructionBootstrapper>().SeedAsync();
}

// Behind a TLS-terminating proxy/edge (DO App Platform, nginx, etc.) HTTPS is enforced upstream and
// the container only speaks HTTP — an in-app redirect would loop or fail health checks. Self-host
// direct mode (no forwarded headers trusted) still does its own redirect.
if (!useForwardedHeaders)
{
    app.UseHttpsRedirection();
}

// Serve the built SPA same-origin (so the BFF cookie + OIDC redirects work). Static assets are
// served before auth; the SPA fallback is anonymous so the shell loads and drives the login flow.
app.UseDefaultFiles();
app.UseStaticFiles();

// CORS must sit after routing and before auth. Only the ingest endpoint carries an [EnableCors]
// policy, so this is a no-op for every other (same-origin) endpoint and for the SPA.
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
app.MapHub<BoardHub>("/hubs/board");
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
