using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Local;

/// <summary>
/// Lets <c>dotnet ef</c> build the context for migrations without running the app
/// (so it never tries to reach Postgres at design time).
/// </summary>
public sealed class LocalAuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LocalAuthDbContext>
{
    public LocalAuthDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<LocalAuthDbContext> options = new DbContextOptionsBuilder<LocalAuthDbContext>()
            .UseNpgsql("Host=localhost;Database=providerwrapper;Username=wrapper;Password=wrapper")
            .Options;

        return new LocalAuthDbContext(options);
    }
}
