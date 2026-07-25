using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dimes.Tests;

/// <summary>Boots the real API pipeline in-process against a throwaway SQLite file, so tests can exercise
/// the things that only exist at the HTTP layer: the authorization attributes, the fallback policy that
/// makes every endpoint authenticated by default, and cookie sessions. Service-level tests can't see any
/// of that — <c>ProjectService</c> never knows whether a request was authenticated.
///
/// Local auth mode with a seeded site admin, because <see cref="AuthBootstrapper"/> is the only way to get
/// a first credential; every other user is created through the admin API.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin-pw-1";

    /// <summary>Matches the API's wire format: camelCase with enums as strings (Program.cs configures a
    /// JsonStringEnumConverter). Reading a response with default options would fail on any enum.</summary>
    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"dimes-test-{Guid.NewGuid():N}.db");

    /// <summary>Settings must reach Program.cs, which reads the connection string while the
    /// WebApplicationBuilder is still being constructed. Production rather than Development because the
    /// dev config pins Auth:Mode to Oidc, which has no local login to drive.</summary>
    private Dictionary<string, string?> Settings => new()
    {
        [HostDefaults.EnvironmentKey] = Environments.Production,
        ["ConnectionStrings:Dimes"] = $"Data Source={_dbPath}",
        ["Auth:Mode"] = "Local",
        ["Auth:SiteAdmin:Email"] = AdminEmail,
        ["Auth:SiteAdmin:InitialPassword"] = AdminPassword,
    };

    /// <summary>Host configuration, not app configuration. Under minimal hosting the entry point builds
    /// its own WebApplicationBuilder and reads ConnectionStrings:Dimes during that construction, which is
    /// before ConfigureWebHost/ConfigureAppConfiguration callbacks run — so settings applied there arrive
    /// too late and Program.cs silently falls back to the default &lt;contentRoot&gt;/data/dimes.db. That
    /// default is a developer's real database, so getting this wrong is not merely a broken test.</summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Belt and braces: also apply them as app configuration, so anything read after the build sees
        // the same values.
        builder.UseEnvironment(Environments.Production);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
    }

    /// <summary>A client that keeps cookies, so a login carries into later requests. Redirects are off —
    /// the API answers auth failures with 401/403 status codes and a redirect would mask them.
    ///
    /// The base address is https deliberately. Outside Development the session cookie is issued with
    /// <c>CookieSecurePolicy.Always</c>, and a cookie container won't send a Secure cookie back over
    /// http — every authenticated request would silently arrive anonymous. Using https here keeps the
    /// production cookie policy under test instead of relaxing it for the test host.</summary>
    public HttpClient CreateSessionClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    /// <summary>Sign in and return the authenticated client. Throws on failure so a broken login surfaces
    /// as itself rather than as a confusing 401 in the assertion under test.</summary>
    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = CreateSessionClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        await EnsureOkAsync(response, $"login as {email}");
        return client;
    }

    /// <summary>EnsureSuccessStatusCode hides the ProblemDetails body, which is where the API says what
    /// actually went wrong — leaving a bare "400 Bad Request" to debug. Include it.</summary>
    private static async Task EnsureOkAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Could not {what}: {(int)response.StatusCode} {response.StatusCode}. {body}");
    }

    /// <summary>Create a non-admin user through the admin API and sign in as them.</summary>
    public async Task<HttpClient> CreateUserAndLoginAsync(HttpClient adminClient, string displayName, string email)
    {
        const string password = "User-pw-1";
        var created = await adminClient.PostAsJsonAsync(
            "/api/admin/users", new { displayName, email, password, isSiteAdmin = false });
        await EnsureOkAsync(created, $"create user {email}");
        return await LoginAsync(email, password);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }
        // The host holds the SQLite file until it's torn down, which base.Dispose has just done.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test run over.
            }
        }
    }
}
