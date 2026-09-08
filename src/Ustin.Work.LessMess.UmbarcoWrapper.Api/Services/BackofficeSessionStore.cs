using System.Collections.Concurrent;

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
///     In-memory store for the v2 back-office BFF cookie set. Kept separate from
///     the v1 <see cref="InMemorySessionStore"/> so the two auth models stay fully
///     independent and can be exercised side by side.
/// </summary>
public sealed class InMemoryBackofficeSessionStore : IBackofficeSessionStore
{
    private readonly ConcurrentDictionary<string, BackofficeSessionRecord> _sessions = new();

    public Task<string> CreateAsync(string username, BffSession session, string? ip)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _sessions[sessionId] = new BackofficeSessionRecord(
            username, session.Cookies, session.ExpiresAtUtc, ip, now, now);
        return Task.FromResult(sessionId);
    }

    public Task<BackofficeSessionRecord?> GetAsync(string sessionId) =>
        Task.FromResult(_sessions.GetValueOrDefault(sessionId));

    public Task RefreshAsync(string sessionId, BffSession session)
    {
        if (_sessions.TryGetValue(sessionId, out BackofficeSessionRecord? existing))
        {
            _sessions[sessionId] = existing with
            {
                Cookies = session.Cookies,
                ExpiresAtUtc = session.ExpiresAtUtc,
                LastSeenUtc = DateTimeOffset.UtcNow,
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
