namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth;

/// <summary>
/// The authentication operations the wrapper exposes to the mobile app. Two
/// implementations exist behind the <c>MemberAuth:Mode</c> switch:
/// <list type="bullet">
///   <item><see cref="Local.LocalMemberAuthProvider"/> — a self-contained identity
///   store on a local Postgres database (test / offline mode, no upstream).</item>
///   <item><see cref="Proxy.ProxyMemberAuthProvider"/> — forwards every call to a
///   real Umbraco instance.</item>
/// </list>
/// Both honour the exact same HTTP contract so the client cannot tell them apart.
/// </summary>
public interface IMemberAuthProvider
{
    Task<AuthResult<RegisterResult>> RegisterAsync(RegisterRequest request, AuthCallContext ctx, CancellationToken ct);

    Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, AuthCallContext ctx, CancellationToken ct);

    Task<AuthResult<TokenResponse>> RefreshAsync(RefreshRequest request, AuthCallContext ctx, CancellationToken ct);

    Task<AuthResult<Unit>> LogoutAsync(LogoutRequest request, Guid? memberKey, CancellationToken ct);

    Task<AuthResult<MessageResult>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);

    Task<AuthResult<Unit>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);

    Task<AuthResult<Unit>> ChangePasswordAsync(ChangePasswordRequest request, Guid memberKey, CancellationToken ct);

    Task<AuthResult<Unit>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct);

    Task<AuthResult<MessageResult>> ResendConfirmationAsync(ForgotPasswordRequest request, CancellationToken ct);

    Task<AuthResult<MemberProfile>> MeAsync(Guid memberKey, CancellationToken ct);
}
