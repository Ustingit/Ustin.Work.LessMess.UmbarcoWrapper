using System.ComponentModel.DataAnnotations;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;

/// <summary>Persistence model for one audit entry (table <c>AuditLog</c>).</summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    [MaxLength(120)]
    public string Event { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Username { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    [MaxLength(64)]
    public string Outcome { get; set; } = string.Empty;

    public string? Detail { get; set; }
}
