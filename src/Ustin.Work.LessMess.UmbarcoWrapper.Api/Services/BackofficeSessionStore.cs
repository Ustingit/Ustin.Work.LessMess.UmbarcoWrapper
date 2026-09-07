using System.Text.Json;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

/// <summary>One v2 session: the Umbraco back-office BFF cookie set, plus trace metadata.</summary>
public sealed record BackofficeSessionRecord(
    string Username,
    string Cookies,
    DateTimeOffset ExpiresAtUtc,
    string? Ip,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastSeenUtc);

public interface IBackofficeSessionStore
{
    Task<string> CreateAsync(string username, BffSession session, string? ip);

    Task<BackofficeSessionRecord?> GetAsync(string sessionId);

    Task RefreshAsync(string sessionId, BffSession session);

    Task RemoveAsync(string sessionId);
}

/// <summary>
///     File-backed (<c>backoffice-sessions.json</c>). Kept separate from the v1
///     <see cref="FileSessionStore"/> so the two auth models stay fully independent
///     and can be exercised side by side.
/// </summary>
public sealed class BackofficeSessionStore : IBackofficeSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public BackofficeSessionStore(WrapperOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _path = Path.Combine(options.DataDirectory, "backoffice-sessions.json");
    }

    public async Task<string> CreateAsync(string username, BffSession session, string? ip)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await MutateAsync(map => map[sessionId] = new BackofficeSessionRecord(
            username, session.Cookies, session.ExpiresAtUtc, ip, now, now));
        return sessionId;
    }

    public async Task<BackofficeSessionRecord?> GetAsync(string sessionId)
    {
        await _gate.WaitAsync();
        try
        {
            return Load().GetValueOrDefault(sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RefreshAsync(string sessionId, BffSession session) =>
        MutateAsync(map =>
        {
            if (map.TryGetValue(sessionId, out BackofficeSessionRecord? existing))
            {
                map[sessionId] = existing with
                {
                    Cookies = session.Cookies,
                    ExpiresAtUtc = session.ExpiresAtUtc,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                };
            }
        });

    public Task RemoveAsync(string sessionId) => MutateAsync(map => map.Remove(sessionId));

    private async Task MutateAsync(Action<Dictionary<string, BackofficeSessionRecord>> mutation)
    {
        await _gate.WaitAsync();
        try
        {
            Dictionary<string, BackofficeSessionRecord> map = Load();
            mutation(map);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(map, JsonOptions));
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, BackofficeSessionRecord> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, BackofficeSessionRecord>();
        }

        var json = File.ReadAllText(_path);
        return string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, BackofficeSessionRecord>()
            : JsonSerializer.Deserialize<Dictionary<string, BackofficeSessionRecord>>(json)
              ?? new Dictionary<string, BackofficeSessionRecord>();
    }
}
