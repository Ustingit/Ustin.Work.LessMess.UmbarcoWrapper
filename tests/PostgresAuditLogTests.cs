using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auditing;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Tests;

public sealed class PostgresAuditLogTests
{
    private static WrapperDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WrapperDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task WriteAsync_persists_a_row_with_the_expected_fields()
    {
        await using WrapperDbContext db = NewDb();
        var log = new PostgresAuditLog(db, NullLogger<PostgresAuditLog>.Instance);

        await log.WriteAsync(new AuditEntry("login", "alice", "10.0.0.5", "curl/8", "success", "cookie stored")
        {
            Timestamp = DateTimeOffset.Parse("2026-09-02T18:20:01.512Z"),
        });

        AuditLogEntry row = Assert.Single(db.AuditLog);
        Assert.Equal("login", row.Event);
        Assert.Equal("alice", row.Username);
        Assert.Equal("10.0.0.5", row.Ip);
        Assert.Equal("curl/8", row.UserAgent);
        Assert.Equal("success", row.Outcome);
        Assert.Equal("cookie stored", row.Detail);
        Assert.Equal(DateTimeOffset.Parse("2026-09-02T18:20:01.512Z"), row.TimestampUtc);
    }

    [Fact]
    public async Task WriteAsync_appends_one_row_per_call()
    {
        await using WrapperDbContext db = NewDb();
        var log = new PostgresAuditLog(db, NullLogger<PostgresAuditLog>.Instance);

        await log.WriteAsync(new AuditEntry("register", "bob", null, null, "success"));
        await log.WriteAsync(new AuditEntry("login", "bob", null, null, "success"));
        await log.WriteAsync(new AuditEntry("logout", "bob", null, null, "success"));

        Assert.Equal(new[] { "register", "login", "logout" }, db.AuditLog.OrderBy(x => x.Id).Select(x => x.Event));
    }

    [Fact]
    public async Task Null_optional_fields_are_stored_as_null()
    {
        await using WrapperDbContext db = NewDb();
        var log = new PostgresAuditLog(db, NullLogger<PostgresAuditLog>.Instance);

        await log.WriteAsync(new AuditEntry("login.failed", "eve", null, null, "failure"));

        AuditLogEntry row = Assert.Single(db.AuditLog);
        Assert.Null(row.Ip);
        Assert.Null(row.UserAgent);
        Assert.Null(row.Detail);
    }
}
