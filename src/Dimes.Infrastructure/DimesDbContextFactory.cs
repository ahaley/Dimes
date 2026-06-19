using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dimes.Infrastructure;

/// <summary>Design-time factory so <c>dotnet ef</c> can build the context (and generate migrations)
/// without booting the API host. Uses a throwaway SQLite file; the runtime connection string is
/// supplied by the host via <see cref="ServiceCollectionExtensions.AddDimesPersistence"/>.</summary>
public class DimesDbContextFactory : IDesignTimeDbContextFactory<DimesDbContext>
{
    public DimesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DimesDbContext>()
            .UseSqlite("Data Source=dimes.design.db")
            .Options;
        return new DimesDbContext(options);
    }
}
