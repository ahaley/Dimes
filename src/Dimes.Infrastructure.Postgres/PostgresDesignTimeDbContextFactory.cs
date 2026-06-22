using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dimes.Infrastructure.Postgres;

/// <summary>Design-time factory used only by <c>dotnet ef</c> to generate/manage the Postgres
/// migration set in this assembly. The connection string is a throwaway — migrations are authored
/// offline. At runtime the host picks the provider via
/// <see cref="ServiceCollectionExtensions.AddDimesPersistence"/>, not this factory.</summary>
public class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DimesDbContext>
{
    public DimesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DimesDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=dimes_design;Username=postgres",
                npg => npg.MigrationsAssembly(DatabaseConnection.PostgresMigrationsAssembly))
            .Options;
        return new DimesDbContext(options);
    }
}
