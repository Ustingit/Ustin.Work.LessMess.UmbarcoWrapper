using System.Text.Json;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

/// <summary>One logged-in wrapper session: the Umbraco member cookie plus trace metadata.</summary>
public sealed record SessionRecord(
    string Username,
    string Email,
    string Cookies,
    string? Ip,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastSeenUtc);

public interface ISessionStore
{
    Task<string> CreateAsync(string username, string email, string cookies, string? ip);

    Task<SessionRecord?> GetAsync(string sessionId);

    Task TouchAsync(string sessionId, string? cookies = null);

    Task RemoveAsync(string sessionId);
}

/// <summary>
///     File-backed session map (<c>{DataDirectory}/sessions.json</c>). No database:
///     the wrapper keeps only the mapping wrapper-session-id → raw Umbraco Set-Cookie
///     values, so it can replay them as the logged-in member on later calls.
/// </summary>
public sealed class FileSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public FileSessionStore(WrapperOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _path = Path.Combine(options.DataDirectory, "sessions.json");
    }

    public async Task<string> CreateAsync(string username, string email, string cookies, string? ip)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        await MutateAsync(map => map[sessionId] = new SessionRecord(username, email, cookies, ip, now, now));
        return sessionId;
    }

    public async Task<SessionRecord?> GetAsync(string sessionId)
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

    public Task TouchAsync(string sessionId, string? cookies = null) =>
        MutateAsync(map =>
        {
            if (map.TryGetValue(sessionId, out SessionRecord? existing))
            {
                map[sessionId] = existing with
                {
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    Cookies = string.IsNullOrEmpty(cookies) ? existing.Cookies : cookies,
                };
            }
        });

    public Task RemoveAsync(string sessionId) => MutateAsync(map => map.Remove(sessionId));

    private async Task MutateAsync(Action<Dictionary<string, SessionRecord>> mutation)
    {
        await _gate.WaitAsync();
        try
        {
            Dictionary<string, SessionRecord> map = Load();
            mutation(map);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(map, JsonOptions));
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, SessionRecord> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, SessionRecord>();
        }

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, SessionRecord>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, SessionRecord>>(json)
               ?? new Dictionary<string, SessionRecord>();
    }
}
