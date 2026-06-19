using Dimes.Domain.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dimes.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Register the Dimes persistence context (SQLite) and the domain lifecycle engine.</summary>
    public static IServiceCollection AddDimesPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DimesDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<LifecycleService>();
        return services;
    }
}
