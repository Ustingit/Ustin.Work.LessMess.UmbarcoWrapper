using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

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
///     Backed by a dedicated <see cref="MemoryCache"/> so entries expire on their
///     own (idle + absolute cap) and total size is bounded. State lives only while
///     the process runs; a restart signs everyone out — the credential proof is
///     the upstream cookie, not our copy. Swap to <c>HybridCache</c> + Redis to
///     share sessions across instances without touching this interface.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly SessionCacheOptions _options;

    public InMemorySessionStore(IOptions<SessionCacheOptions> options)
    {
        _options = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _options.MaxEntries });
    }

    public Task<string> CreateAsync(string username, string email, string cookies, string? ip)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        Set(sessionId, new SessionRecord(username, email, cookies, ip, now, now));
        return Task.FromResult(sessionId);
    }

    public Task<SessionRecord?> GetAsync(string sessionId) =>
        Task.FromResult(_cache.TryGetValue(sessionId, out SessionRecord? record) ? record : null);

    public Task TouchAsync(string sessionId, string? cookies = null)
    {
        if (_cache.TryGetValue(sessionId, out SessionRecord? existing) && existing is not null)
        {
            Set(sessionId, existing with
            {
                LastSeenUtc = DateTimeOffset.UtcNow,
                Cookies = string.IsNullOrEmpty(cookies) ? existing.Cookies : cookies,
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

    private void Set(string sessionId, SessionRecord record) =>
        _cache.Set(sessionId, record, new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = _options.IdleTimeout,
            // Absolute cap is computed from CreatedUtc so a Touch cannot push it out.
            AbsoluteExpiration = record.CreatedUtc + _options.MaxLifetime,
        });
}
