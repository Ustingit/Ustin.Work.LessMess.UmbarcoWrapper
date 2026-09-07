namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

/// <summary>Request-scoped helper around the <c>wrapper_bo_sid</c> cookie for the v2 (back-office BFF) flow.</summary>
public sealed class BackofficeSession
{
    public const string CookieName = "wrapper_bo_sid";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IBackofficeSessionStore _store;
    private readonly BackofficeOAuthClient _oauth;

    public BackofficeSession(
        IHttpContextAccessor httpContextAccessor,
        IBackofficeSessionStore store,
        BackofficeOAuthClient oauth)
    {
        _httpContextAccessor = httpContextAccessor;
        _store = store;
        _oauth = oauth;
    }

    private HttpContext Context =>
        _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP context");

    public string? SessionId => Context.Request.Cookies[CookieName];

    public string? ClientIp => Context.Connection.RemoteIpAddress?.ToString();

    public string UserAgent => Context.Request.Headers.UserAgent.ToString();

    public async Task<string> StartAsync(string username, BffSession session)
    {
        var sessionId = await _store.CreateAsync(username, session, ClientIp);
        Context.Response.Cookies.Append(CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
        return sessionId;
    }

    /// <summary>The current record with a non-expired back-office session (refreshing if needed), or null.</summary>
    public async Task<BackofficeSessionRecord?> GetValidAsync()
    {
        if (SessionId is not { Length: > 0 } id)
        {
            return null;
        }

        BackofficeSessionRecord? record = await _store.GetAsync(id);
        if (record is null)
        {
            return null;
        }

        if (record.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return record;
        }

        BffSession? refreshed = await _oauth.RefreshAsync(record.Cookies);
        if (refreshed is null)
        {
            return record; // let the caller hit 401 and clean up
        }

        await _store.RefreshAsync(id, refreshed);
        return record with { Cookies = refreshed.Cookies, ExpiresAtUtc = refreshed.ExpiresAtUtc };
    }

    public async Task EndAsync()
    {
        if (SessionId is { Length: > 0 } id)
        {
            await _store.RemoveAsync(id);
        }

        Context.Response.Cookies.Delete(CookieName);
    }
}
