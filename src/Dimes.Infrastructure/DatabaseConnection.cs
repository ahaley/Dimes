using Npgsql;

namespace Dimes.Infrastructure;

/// <summary>The relational backends Dimes supports. SQLite is the tested default (local dev, tests,
/// light self-host); Postgres targets heavier / cloud installs (e.g. DO App Platform).</summary>
public enum DatabaseProvider
{
    Sqlite,
    Postgres,
}

/// <summary>Connection-string helpers: pick the provider from a connection string and normalize the
/// <c>postgresql://</c> URI that managed Postgres platforms hand out into the keyword form Npgsql
/// accepts.</summary>
public static class DatabaseConnection
{
    /// <summary>Assembly that holds the Postgres-specific EF migrations (kept separate from the
    /// SQLite migrations in this assembly, since a context has one model snapshot per assembly).</summary>
    public const string PostgresMigrationsAssembly = "Dimes.Infrastructure.Postgres";

    public static DatabaseProvider Detect(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return DatabaseProvider.Sqlite;
        }

        var s = connectionString.TrimStart();
        if (s.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.Postgres;
        }

        // Npgsql keyword form uses Host=/Server=; SQLite uses Data Source=/Filename=.
        if (s.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.Postgres;
        }

        return DatabaseProvider.Sqlite;
    }

    /// <summary>Managed Postgres (DO App Platform, Heroku, etc.) exposes the connection as a
    /// <c>postgresql://user:pass@host:port/db?sslmode=require</c> URI, which Npgsql does not accept
    /// directly. Convert it to a keyword connection string; pass keyword strings through unchanged.</summary>
    public static string NormalizePostgres(string connectionString)
    {
        var s = connectionString.Trim();
        if (!s.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(s);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        // Honor an explicit ?sslmode=; default managed Postgres to require TLS.
        var sslMode = SslMode.Require;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 &&
                kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<SslMode>(kv[1], ignoreCase: true, out var parsed))
            {
                sslMode = parsed;
            }
        }

        // SslMode.Require (the managed-Postgres default) encrypts without validating the server cert,
        // which suits DO's CA that isn't in the base image trust store. For full verification, pass
        // ?sslmode=verify-full and supply the CA via RootCertificate.
        builder.SslMode = sslMode;

        return builder.ConnectionString;
    }
}
