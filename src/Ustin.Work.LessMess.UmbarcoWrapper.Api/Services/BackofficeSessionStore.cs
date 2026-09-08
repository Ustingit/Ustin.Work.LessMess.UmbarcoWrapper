using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

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
///     In-memory store for the v2 back-office BFF cookie set, on its own
///     <see cref="MemoryCache"/> — kept separate from the v1
///     <see cref="InMemorySessionStore"/> so the two auth models stay fully
///     independent. The absolute expiry is clamped to the BFF cookie set's own
///     expiry (a session past that is useless); <see cref="RefreshAsync"/> extends
///     it when the cookie set is renewed.
/// </summary>
public sealed class InMemoryBackofficeSessionStore : IBackofficeSessionStore, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly SessionCacheOptions _options;

    public InMemoryBackofficeSessionStore(IOptions<SessionCacheOptions> options)
    {
        _options = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _options.MaxEntries });
    }

    public Task<string> CreateAsync(string username, BffSession session, string? ip)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        Set(sessionId, new BackofficeSessionRecord(
            username, session.Cookies, session.ExpiresAtUtc, ip, now, now));
        return Task.FromResult(sessionId);
    }

    public Task<BackofficeSessionRecord?> GetAsync(string sessionId) =>
        Task.FromResult(_cache.TryGetValue(sessionId, out BackofficeSessionRecord? record) ? record : null);

    public Task RefreshAsync(string sessionId, BffSession session)
    {
        if (_cache.TryGetValue(sessionId, out BackofficeSessionRecord? existing) && existing is not null)
        {
            Set(sessionId, existing with
            {
                Cookies = session.Cookies,
                ExpiresAtUtc = session.ExpiresAtUtc,
                LastSeenUtc = DateTimeOffset.UtcNow,
            });
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId)
    {
        _cache.Remove(sessionId);
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();

    private void Set(string sessionId, BackofficeSessionRecord record)
    {
        DateTimeOffset absolute = record.CreatedUtc + _options.MaxLifetime;
        if (record.ExpiresAtUtc < absolute)
        {
            absolute = record.ExpiresAtUtc;
        }

        _cache.Set(sessionId, record, new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = _options.IdleTimeout,
            AbsoluteExpiration = absolute,
        });
    }
}
