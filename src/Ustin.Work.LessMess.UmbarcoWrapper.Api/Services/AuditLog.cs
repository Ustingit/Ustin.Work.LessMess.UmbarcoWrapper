using System.Text.Json;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

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

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry);
}

/// <summary>
///     Append-only, one JSON object per line, under <c>{DataDirectory}/audit.log</c>.
///     Also mirrored to <see cref="ILogger"/> so it shows up in <c>docker logs</c>.
///     This is the wrapper's whole "persistence" story alongside the session map.
/// </summary>
public sealed class FileAuditLog : IAuditLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string _path;
    private readonly ILogger<FileAuditLog> _logger;

    public FileAuditLog(WrapperOptions options, ILogger<FileAuditLog> logger)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _path = Path.Combine(options.DataDirectory, "audit.log");
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry)
    {
        var line = JsonSerializer.Serialize(new
        {
            ts = entry.Timestamp.ToUniversalTime().ToString("O"),
            @event = entry.Event,
            username = entry.Username,
            ip = entry.Ip,
            userAgent = entry.UserAgent,
            outcome = entry.Outcome,
            detail = entry.Detail,
        });

        await Gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, line + Environment.NewLine);
        }
        finally
        {
            Gate.Release();
        }

        _logger.LogInformation(
            "audit event={Event} user={Username} ip={Ip} outcome={Outcome} detail={Detail}",
            entry.Event,
            entry.Username ?? "-",
            entry.Ip ?? "-",
            entry.Outcome,
            entry.Detail ?? "-");
    }
}
