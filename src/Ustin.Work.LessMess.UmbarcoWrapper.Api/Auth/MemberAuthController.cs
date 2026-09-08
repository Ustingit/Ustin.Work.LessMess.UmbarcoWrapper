using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;
using Microsoft.Extensions.Options;

using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Auth;

/// <summary>
/// Authentication API surfaced to the mobile app. The behaviour behind it is
/// chosen by <c>MemberAuth:Mode</c> (Local Postgres store vs. proxy to Umbraco);
/// the contract here is identical either way.
/// </summary>
[ApiController]
[Route("api/member-auth/v1")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class MemberAuthController : ControllerBase
{
    private readonly IMemberAuthProvider _provider;
    private readonly MemberAuthOptions _options;

    public MemberAuthController(IMemberAuthProvider provider, IOptions<MemberAuthOptions> options)
    {
        _provider = provider;
        _options = options.Value;
    }

    private AuthCallContext Ctx => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) =>
        Map(await _provider.RegisterAsync(request, Ctx, ct));

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) =>
        Map(await _provider.LoginAsync(request, Ctx, ct));

    [HttpPost("token/refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct) =>
        Map(await _provider.RefreshAsync(request, Ctx, ct));

    [HttpPost("password/forgot")]
    [EnableRateLimiting(RateLimitPolicies.PasswordForgot)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        AuthResult<MessageResult> r = await _provider.ForgotPasswordAsync(request, ct);
        return r.Ok ? Accepted(r.Value) : Problem(statusCode: r.Status, title: r.Title, detail: r.Detail);
    }

    [HttpPost("password/reset")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct) =>
        MapNoContent(await _provider.ResetPasswordAsync(request, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        if (!TryRequireMember(out Guid? memberKey, out IActionResult? challenge))
        {
            return challenge!;
        }

        return MapNoContent(await _provider.LogoutAsync(request, memberKey, ct));
    }

    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!TryRequireMember(out Guid? memberKey, out IActionResult? challenge))
        {
            return challenge!;
        }

        return MapNoContent(await _provider.ChangePasswordAsync(request, memberKey ?? Guid.Empty, ct));
    }

    [HttpPost("email/confirm")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken ct) =>
        MapNoContent(await _provider.ConfirmEmailAsync(request, ct));

    [HttpPost("email/resend")]
    [EnableRateLimiting(RateLimitPolicies.PasswordForgot)]
    public async Task<IActionResult> ResendConfirmation(ForgotPasswordRequest request, CancellationToken ct)
    {
        AuthResult<MessageResult> r = await _provider.ResendConfirmationAsync(request, ct);
        return r.Ok ? Accepted(r.Value) : Problem(statusCode: r.Status, title: r.Title, detail: r.Detail);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!TryRequireMember(out Guid? memberKey, out IActionResult? challenge))
        {
            return challenge!;
        }

        return Map(await _provider.MeAsync(memberKey ?? Guid.Empty, ct));
    }

    // ---- helpers -----------------------------------------------------

    /// <summary>
    /// Local mode: require a valid wrapper-issued token and pull the member key
    /// from it. Proxy mode: the upstream is the token authority, so just relay.
    /// </summary>
    private bool TryRequireMember(out Guid? memberKey, out IActionResult? challenge)
    {
        memberKey = null;
        challenge = null;

        if (_options.Mode == AuthMode.Proxy)
        {
            return true;
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            challenge = Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not authenticated");
            return false;
        }

        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out Guid key))
        {
            challenge = Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Token missing subject");
            return false;
        }

        memberKey = key;
        return true;
    }

    private IActionResult Map<T>(AuthResult<T> r) =>
        r.Ok ? Ok(r.Value) : Problem(statusCode: r.Status, title: r.Title, detail: r.Detail);

    private IActionResult MapNoContent(AuthResult<Unit> r) =>
        r.Ok ? NoContent() : Problem(statusCode: r.Status, title: r.Title, detail: r.Detail);
}
