using System.Text.Json.Serialization;
using Dimes.Api;
using Dimes.Api.Auth;
using Dimes.Api.Services;
using Dimes.Infrastructure;
using Dimes.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Resolve the database connection. An explicit ConnectionStrings:Dimes (env or config) wins —
// e.g. an absolute path, or Postgres later. Otherwise default to an absolute, cwd-independent SQLite
// file under <contentRoot>/data so projects persist across runs no matter where the app is launched.
var connectionString = builder.Configuration.GetConnectionString("Dimes");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Directory.CreateDirectory(dataDir);
    connectionString = $"Data Source={Path.Combine(dataDir, "dimes.db")}";
}

builder.Services.AddDimesPersistence(connectionString);
builder.Services.AddDimesProviders();

// Authentication: cookie session backed by local login or Keycloak OIDC, chosen via Auth:Mode.
builder.Services.AddDimesAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(o => o.AddDimesAuthorizationPolicies());

// Application services (capture → inbox → promote → lifecycle, commentary, SCM context).
builder.Services.AddScoped<MembershipResolver>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ObservationService>();
builder.Services.AddScoped<ChangeRequestService>();
builder.Services.AddScoped<CommentaryService>();
builder.Services.AddScoped<ScmService>();
builder.Services.AddScoped<SiteAdminService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

// Apply pending migrations on startup, then seed the bootstrap site admin from config, so a fresh
// self-host install needs no manual DB step and has a way in.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DimesDbContext>();
    db.Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<AuthBootstrapper>().SeedAsync();
}

app.UseHttpsRedirection();

// Serve the built SPA same-origin (so the BFF cookie + OIDC redirects work). Static assets are
// served before auth; the SPA fallback is anonymous so the shell loads and drives the login flow.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
