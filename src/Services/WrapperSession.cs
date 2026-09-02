namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

/// <summary>
///     Request-scoped helper around the <c>wrapper_sid</c> cookie and the file session store.
///     Also the single place that reads client IP / user agent for the audit trail.
/// </summary>
public sealed class WrapperSession
{
    public const string CookieName = "wrapper_sid";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISessionStore _store;

    public WrapperSession(IHttpContextAccessor httpContextAccessor, ISessionStore store)
    {
        _httpContextAccessor = httpContextAccessor;
        _store = store;
    }

    private HttpContext Context =>
        _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP context");

    public string? SessionId => Context.Request.Cookies[CookieName];

    public string? ClientIp => Context.Connection.RemoteIpAddress?.ToString();

    public string UserAgent => Context.Request.Headers.UserAgent.ToString();

    public Task<SessionRecord?> GetAsync() =>
        SessionId is { Length: > 0 } id ? _store.GetAsync(id) : Task.FromResult<SessionRecord?>(null);

    public async Task<string> StartAsync(string username, string email, string cookies)
    {
        var sessionId = await _store.CreateAsync(username, email, cookies, ClientIp);
        Context.Response.Cookies.Append(CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
        return sessionId;
    }

    public Task TouchAsync() =>
        SessionId is { Length: > 0 } id ? _store.TouchAsync(id) : Task.CompletedTask;

    public async Task EndAsync()
    {
        if (SessionId is { Length: > 0 } id)
        {
            await _store.RemoveAsync(id);
        }

        Context.Response.Cookies.Delete(CookieName);
    }
}
