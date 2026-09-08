using Microsoft.EntityFrameworkCore;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;

/// <summary>
/// Database A — the durable store. Today it holds only the audit log; device
/// data, licences and terms-acceptance tables land here later.
/// </summary>
public sealed class WrapperDbContext : DbContext
{
    public WrapperDbContext(DbContextOptions<WrapperDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => x.Event);
        });
    }
}
