namespace Ustin.Work.LessMess.UmbarcoWrapper.Core.Auditing;

/// <summary>A single traceability record: who did what, when, from where, with what outcome.</summary>
public sealed record AuditEntry(
    string Event,
    string? Username,
    string? Ip,
    string? UserAgent,
    string Outcome,
    string? Detail = null)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Writes audit entries to the durable store (database A) and mirrors them to
/// <c>ILogger</c>. Exists in every mode.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry);
}
