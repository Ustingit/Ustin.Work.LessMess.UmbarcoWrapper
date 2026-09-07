using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Proxy;

/// <summary>
/// Real mode: relays every call to an upstream Umbraco member-auth API
/// (<c>/api/member-auth/v1/*</c>). Anonymous calls forward the body; authenticated
/// calls also forward the caller's <c>Authorization</c> header. Tokens are minted
/// by the upstream, not here.
/// </summary>
public sealed class ProxyMemberAuthProvider : IMemberAuthProvider
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<ProxyMemberAuthProvider> _logger;

    public ProxyMemberAuthProvider(
        HttpClient http,
        IHttpContextAccessor httpContext,
        ILogger<ProxyMemberAuthProvider> logger)
    {
        _http = http;
        _httpContext = httpContext;
        _logger = logger;
    }

    public Task<AuthResult<RegisterResult>> RegisterAsync(RegisterRequest request, AuthCallContext ctx, CancellationToken ct) =>
        PostAsync<RegisterResult>("api/member-auth/v1/register", request, forwardAuth: false, ct);

    public Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, AuthCallContext ctx, CancellationToken ct) =>
        PostAsync<TokenResponse>("api/member-auth/v1/login", request, forwardAuth: false, ct);

    public Task<AuthResult<TokenResponse>> RefreshAsync(RefreshRequest request, AuthCallContext ctx, CancellationToken ct) =>
        PostAsync<TokenResponse>("api/member-auth/v1/token/refresh", request, forwardAuth: false, ct);

    public Task<AuthResult<Unit>> LogoutAsync(LogoutRequest request, Guid? memberKey, CancellationToken ct) =>
        PostNoContentAsync("api/member-auth/v1/logout", request, forwardAuth: true, ct);

    public Task<AuthResult<MessageResult>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct) =>
        PostAsync<MessageResult>("api/member-auth/v1/password/forgot", request, forwardAuth: false, ct);

    public Task<AuthResult<Unit>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct) =>
        PostNoContentAsync("api/member-auth/v1/password/reset", request, forwardAuth: false, ct);

    public Task<AuthResult<Unit>> ChangePasswordAsync(ChangePasswordRequest request, Guid memberKey, CancellationToken ct) =>
        PostNoContentAsync("api/member-auth/v1/password/change", request, forwardAuth: true, ct);

    public Task<AuthResult<Unit>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct) =>
        PostNoContentAsync("api/member-auth/v1/email/confirm", request, forwardAuth: false, ct);

    public Task<AuthResult<MessageResult>> ResendConfirmationAsync(ForgotPasswordRequest request, CancellationToken ct) =>
        PostAsync<MessageResult>("api/member-auth/v1/email/resend", request, forwardAuth: false, ct);

    public async Task<AuthResult<MemberProfile>> MeAsync(Guid memberKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/member-auth/v1/me");
        ForwardAuth(req);
        using HttpResponseMessage res = await _http.SendAsync(req, ct);
        return await ReadAsync<MemberProfile>(res, ct);
    }

    private async Task<AuthResult<T>> PostAsync<T>(string path, object body, bool forwardAuth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (forwardAuth)
        {
            ForwardAuth(req);
        }

        using HttpResponseMessage res = await _http.SendAsync(req, ct);
        return await ReadAsync<T>(res, ct);
    }

    private async Task<AuthResult<Unit>> PostNoContentAsync(string path, object body, bool forwardAuth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (forwardAuth)
        {
            ForwardAuth(req);
        }

        using HttpResponseMessage res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode
            ? AuthResult<Unit>.Success(Unit.Value)
            : AuthResult<Unit>.Fail((int)res.StatusCode, res.ReasonPhrase ?? "Upstream error", await res.Content.ReadAsStringAsync(ct));
    }

    private static async Task<AuthResult<T>> ReadAsync<T>(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
        {
            T? value = await res.Content.ReadFromJsonAsync<T>(Json, ct);
            return value is null
                ? AuthResult<T>.Fail((int)HttpStatusCode.BadGateway, "Empty upstream response")
                : AuthResult<T>.Success(value);
        }

        var detail = await res.Content.ReadAsStringAsync(ct);
        return AuthResult<T>.Fail((int)res.StatusCode, res.ReasonPhrase ?? "Upstream error", detail);
    }

    private void ForwardAuth(HttpRequestMessage req)
    {
        var header = _httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(header) && AuthenticationHeaderValue.TryParse(header, out AuthenticationHeaderValue? parsed))
        {
            req.Headers.Authorization = parsed;
        }
        else
        {
            _logger.LogDebug("proxy: no Authorization header to forward");
        }
    }
}
