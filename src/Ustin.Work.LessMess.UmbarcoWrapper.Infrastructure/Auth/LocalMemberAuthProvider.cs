using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth.Jwt;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Jwt;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.LocalAuthRepository;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth;

/// <summary>
/// Test / offline mode: a self-contained identity store on Postgres. Password
/// hashing, lockout and the reset / confirmation tokens come from ASP.NET Core
/// Identity; this class adds JWT issuance and refresh-token rotation.
/// </summary>
public sealed class LocalMemberAuthProvider : IMemberAuthProvider
{
    private readonly UserManager<AppUser> _users;
    private readonly LocalAuthDbContext _db;
    private readonly JwtTokenService _tokens;
    private readonly JwtOptions _jwt;
    private readonly MemberAuthOptions _options;
    private readonly ILogger<LocalMemberAuthProvider> _logger;

    public LocalMemberAuthProvider(
        UserManager<AppUser> users,
        LocalAuthDbContext db,
        JwtTokenService tokens,
        IOptions<JwtOptions> jwt,
        IOptions<MemberAuthOptions> options,
        ILogger<LocalMemberAuthProvider> logger)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _jwt = jwt.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResult<RegisterResult>> RegisterAsync(RegisterRequest request, AuthCallContext ctx, CancellationToken ct)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Email = request.Email,
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.Username : request.Name,
        };

        IdentityResult created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            var detail = string.Join("; ", created.Errors.Select(e => e.Description));
            return created.Errors.Any(e => e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
                ? AuthResult<RegisterResult>.Fail(StatusCodes.Status409Conflict, "Account already exists", detail)
                : AuthResult<RegisterResult>.Fail(StatusCodes.Status400BadRequest, "Registration failed", detail);
        }

        IList<string> roles = await _users.GetRolesAsync(user);

        if (_options.RequireEmailConfirmation)
        {
            var token = await _users.GenerateEmailConfirmationTokenAsync(user);
            _logger.LogInformation("member-auth(local): confirm-email token for {Email}: {Token}", request.Email, token);
            return AuthResult<RegisterResult>.Success(new RegisterResult(
                Profile(user, roles), EmailConfirmationRequired: true, Tokens: null,
                DevToken: _options.ExposeTokensInResponses ? token : null));
        }

        TokenResponse? tokens = _options.AutoLoginAfterRegister ? await IssueAsync(user, roles, ctx, ct) : null;
        return AuthResult<RegisterResult>.Success(new RegisterResult(Profile(user, roles), false, tokens, null));
    }

    public async Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, AuthCallContext ctx, CancellationToken ct)
    {
        AppUser? user = await _users.FindByNameAsync(request.UsernameOrEmail)
                        ?? await _users.FindByEmailAsync(request.UsernameOrEmail);

        if (user is null)
        {
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status401Unauthorized, "Invalid credentials");
        }

        if (await _users.IsLockedOutAsync(user))
        {
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status403Forbidden, "Account locked");
        }

        if (!await _users.CheckPasswordAsync(user, request.Password))
        {
            await _users.AccessFailedAsync(user);
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status401Unauthorized, "Invalid credentials");
        }

        await _users.ResetAccessFailedCountAsync(user);

        if (_options.RequireEmailConfirmation && !await _users.IsEmailConfirmedAsync(user))
        {
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status403Forbidden, "Email not confirmed");
        }

        IList<string> roles = await _users.GetRolesAsync(user);
        return AuthResult<TokenResponse>.Success(await IssueAsync(user, roles, ctx, ct));
    }

    public async Task<AuthResult<TokenResponse>> RefreshAsync(RefreshRequest request, AuthCallContext ctx, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        LocalRefreshToken? row = await _db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (row is null)
        {
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status401Unauthorized, "Invalid refresh token", "Unknown");
        }

        if (row.RevokedUtc is not null)
        {
            await RevokeFamilyAsync(row.UserId, ct);
            _logger.LogWarning("member-auth(local): refresh-token reuse for {UserId}; family revoked", row.UserId);
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status401Unauthorized, "Invalid refresh token", "Reused");
        }

        if (row.ExpiresUtc <= DateTime.UtcNow)
        {
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status401Unauthorized, "Invalid refresh token", "Expired");
        }

        AppUser? user = await _users.FindByIdAsync(row.UserId.ToString());
        if (user is null)
        {
            return AuthResult<TokenResponse>.Fail(StatusCodes.Status401Unauthorized, "Member not found");
        }

        IList<string> roles = await _users.GetRolesAsync(user);
        (string rawRefresh, LocalRefreshToken replacement) = NewRefreshRow(user.Id, ctx);

        row.RevokedUtc = DateTime.UtcNow;
        row.ReplacedByTokenHash = replacement.TokenHash;
        _db.RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync(ct);

        AccessToken access = _tokens.Create(
            user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty,
            user.EmailConfirmed, user.Name ?? user.UserName ?? string.Empty, roles);

        return AuthResult<TokenResponse>.Success(new TokenResponse(
            access.Value, "Bearer", (int)_jwt.AccessTokenLifetime.TotalSeconds, access.ExpiresAt,
            rawRefresh, new DateTimeOffset(replacement.ExpiresUtc, TimeSpan.Zero), Profile(user, roles)));
    }

    public async Task<AuthResult<Unit>> LogoutAsync(LogoutRequest request, Guid? memberKey, CancellationToken ct)
    {
        if (request.AllSessions && memberKey is { } key)
        {
            await RevokeFamilyAsync(key, ct);
        }
        else if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            var hash = Hash(request.RefreshToken);
            LocalRefreshToken? row = await _db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
            if (row is { RevokedUtc: null })
            {
                row.RevokedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        return AuthResult<Unit>.Success(Unit.Value);
    }

    public async Task<AuthResult<MessageResult>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        AppUser? user = await _users.FindByEmailAsync(request.Email);
        string? devToken = null;

        if (user is not null)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            _logger.LogInformation("member-auth(local): password-reset token for {Email}: {Token}", request.Email, token);
            devToken = _options.ExposeTokensInResponses ? token : null;
        }

        return AuthResult<MessageResult>.Success(
            new MessageResult("If the account exists, a reset link has been sent.", devToken));
    }

    public async Task<AuthResult<Unit>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        AppUser? user = await _users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthResult<Unit>.Fail(StatusCodes.Status400BadRequest, "Invalid token or email");
        }

        IdentityResult result = await _users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return AuthResult<Unit>.Fail(
                StatusCodes.Status400BadRequest, "Reset failed",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await RevokeFamilyAsync(user.Id, ct);
        return AuthResult<Unit>.Success(Unit.Value);
    }

    public async Task<AuthResult<Unit>> ChangePasswordAsync(ChangePasswordRequest request, Guid memberKey, CancellationToken ct)
    {
        AppUser? user = await _users.FindByIdAsync(memberKey.ToString());
        if (user is null)
        {
            return AuthResult<Unit>.Fail(StatusCodes.Status401Unauthorized, "Not authenticated");
        }

        IdentityResult result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return AuthResult<Unit>.Fail(
                StatusCodes.Status400BadRequest, "Change failed",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await RevokeFamilyAsync(user.Id, ct);
        return AuthResult<Unit>.Success(Unit.Value);
    }

    public async Task<AuthResult<Unit>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct)
    {
        AppUser? user = await _users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthResult<Unit>.Fail(StatusCodes.Status400BadRequest, "Invalid token or email");
        }

        IdentityResult result = await _users.ConfirmEmailAsync(user, request.Token);
        return result.Succeeded
            ? AuthResult<Unit>.Success(Unit.Value)
            : AuthResult<Unit>.Fail(
                StatusCodes.Status400BadRequest, "Confirmation failed",
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<AuthResult<MessageResult>> ResendConfirmationAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        AppUser? user = await _users.FindByEmailAsync(request.Email);
        string? devToken = null;

        if (user is not null && !await _users.IsEmailConfirmedAsync(user))
        {
            var token = await _users.GenerateEmailConfirmationTokenAsync(user);
            _logger.LogInformation("member-auth(local): confirm-email token for {Email}: {Token}", request.Email, token);
            devToken = _options.ExposeTokensInResponses ? token : null;
        }

        return AuthResult<MessageResult>.Success(
            new MessageResult("If the account exists and is unconfirmed, an email has been sent.", devToken));
    }

    public async Task<AuthResult<MemberProfile>> MeAsync(Guid memberKey, CancellationToken ct)
    {
        AppUser? user = await _users.FindByIdAsync(memberKey.ToString());
        return user is null
            ? AuthResult<MemberProfile>.Fail(StatusCodes.Status401Unauthorized, "Not authenticated")
            : AuthResult<MemberProfile>.Success(Profile(user, await _users.GetRolesAsync(user)));
    }

    // ---- helpers ------------------------------------------------------

    private async Task<TokenResponse> IssueAsync(AppUser user, IList<string> roles, AuthCallContext ctx, CancellationToken ct)
    {
        AccessToken access = _tokens.Create(
            user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty,
            user.EmailConfirmed, user.Name ?? user.UserName ?? string.Empty, roles);

        (string rawRefresh, LocalRefreshToken row) = NewRefreshRow(user.Id, ctx);
        _db.RefreshTokens.Add(row);
        await _db.SaveChangesAsync(ct);

        return new TokenResponse(
            access.Value, "Bearer", (int)_jwt.AccessTokenLifetime.TotalSeconds, access.ExpiresAt,
            rawRefresh, new DateTimeOffset(row.ExpiresUtc, TimeSpan.Zero), Profile(user, roles));
    }

    private (string Raw, LocalRefreshToken Row) NewRefreshRow(Guid userId, AuthCallContext ctx)
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        DateTime now = DateTime.UtcNow;
        var row = new LocalRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(raw),
            CreatedUtc = now,
            ExpiresUtc = now.Add(_jwt.RefreshTokenLifetime),
            CreatedByIp = Truncate(ctx.Ip, 45),
            UserAgent = Truncate(ctx.UserAgent, 512),
        };
        return (raw, row);
    }

    private async Task RevokeFamilyAsync(Guid userId, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        await _db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedUtc, now), ct);
    }

    private static MemberProfile Profile(AppUser user, IList<string> roles) =>
        new(user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.Name ?? user.UserName ?? string.Empty,
            user.EmailConfirmed,
            roles.ToArray());

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
