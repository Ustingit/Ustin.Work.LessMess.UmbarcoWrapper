using Microsoft.Extensions.Logging;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auditing;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;

/// <summary>
/// Persists audit entries to database A and mirrors them to <c>ILogger</c>
/// (so <c>docker logs</c> / a log aggregator still see them). Scoped — it holds
/// a <see cref="WrapperDbContext"/>.
/// </summary>
public sealed class PostgresAuditLog : IAuditLog
{
    private readonly WrapperDbContext _db;
    private readonly ILogger<PostgresAuditLog> _logger;

    public PostgresAuditLog(WrapperDbContext db, ILogger<PostgresAuditLog> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry)
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            TimestampUtc = entry.Timestamp.ToUniversalTime(),
            Event = entry.Event,
            Username = entry.Username,
            Ip = entry.Ip,
            UserAgent = entry.UserAgent,
            Outcome = entry.Outcome,
            Detail = entry.Detail,
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "audit event={Event} user={Username} ip={Ip} outcome={Outcome} detail={Detail}",
            entry.Event,
            entry.Username ?? "-",
            entry.Ip ?? "-",
            entry.Outcome,
            entry.Detail ?? "-");
    }
}
