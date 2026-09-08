using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;

/// <summary>Lets <c>dotnet ef</c> build the context without running the app or touching a DB.</summary>
public sealed class WrapperDbContextDesignTimeFactory : IDesignTimeDbContextFactory<WrapperDbContext>
{
    public WrapperDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<WrapperDbContext> options = new DbContextOptionsBuilder<WrapperDbContext>()
            .UseNpgsql("Host=localhost;Database=wrapper;Username=wrapper;Password=wrapper")
            .Options;

        return new WrapperDbContext(options);
    }
}
