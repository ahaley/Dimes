using Dimes.Domain.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dimes.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Register the Dimes persistence context and the domain lifecycle engine. The provider
    /// selects the EF backend: SQLite (default) or Postgres, with Postgres migrations resolved from
    /// the dedicated <see cref="DatabaseConnection.PostgresMigrationsAssembly"/>.</summary>
    public static IServiceCollection AddDimesPersistence(
        this IServiceCollection services, string connectionString, DatabaseProvider provider = DatabaseProvider.Sqlite)
    {
        services.AddDbContext<DimesDbContext>(options =>
        {
            if (provider == DatabaseProvider.Postgres)
            {
                options.UseNpgsql(connectionString, npg => npg.MigrationsAssembly(DatabaseConnection.PostgresMigrationsAssembly));
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });
        services.AddSingleton<LifecycleService>();
        return services;
    }
}
