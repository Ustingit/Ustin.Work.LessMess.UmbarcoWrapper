using System.Collections.Concurrent;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

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
///     In-memory session map: wrapper-session-id → raw Umbraco Set-Cookie values,
///     so the wrapper can replay them as the logged-in member on later calls.
///     Lives only while the process runs; a restart signs everyone out (acceptable
///     for this proxy — the credential proof is the upstream cookie, not our state).
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();

    public Task<string> CreateAsync(string username, string email, string cookies, string? ip)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _sessions[sessionId] = new SessionRecord(username, email, cookies, ip, now, now);
        return Task.FromResult(sessionId);
    }

    public Task<SessionRecord?> GetAsync(string sessionId) =>
        Task.FromResult(_sessions.GetValueOrDefault(sessionId));

    public Task TouchAsync(string sessionId, string? cookies = null)
    {
        if (_sessions.TryGetValue(sessionId, out SessionRecord? existing))
        {
            _sessions[sessionId] = existing with
            {
                LastSeenUtc = DateTimeOffset.UtcNow,
                Cookies = string.IsNullOrEmpty(cookies) ? existing.Cookies : cookies,
            };
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
