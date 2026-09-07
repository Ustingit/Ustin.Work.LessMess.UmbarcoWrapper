using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Local;

/// <summary>
/// Local-mode identity store: ASP.NET Core Identity tables plus one
/// <see cref="LocalRefreshToken"/> table. Postgres.
/// </summary>
public sealed class LocalAuthDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public LocalAuthDbContext(DbContextOptions<LocalAuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<LocalRefreshToken> RefreshTokens => Set<LocalRefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<LocalRefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            e.Property(x => x.CreatedByIp).HasMaxLength(45);
            e.Property(x => x.UserAgent).HasMaxLength(512);
        });
    }
}
