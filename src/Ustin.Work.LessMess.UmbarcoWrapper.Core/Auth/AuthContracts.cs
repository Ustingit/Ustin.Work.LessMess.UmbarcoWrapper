using System.ComponentModel.DataAnnotations;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth;

// ---- requests -----------------------------------------------------------

public sealed class RegisterRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(10)]
    public string Password { get; set; } = string.Empty;

    public string? Name { get; set; }
}

public sealed class LoginRequest
{
    [Required]
    public string UsernameOrEmail { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequest
{
    public string? RefreshToken { get; set; }

    public bool AllSessions { get; set; }
}

public sealed class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(10)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(10)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ConfirmEmailRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;
}

// ---- responses --------------------------------------------------------

public sealed record MemberProfile(
    Guid Key,
    string Username,
    string Email,
    string Name,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    MemberProfile Member);

public sealed record RegisterResult(
    MemberProfile Member,
    bool EmailConfirmationRequired,
    TokenResponse? Tokens,
    string? DevToken);

public sealed record MessageResult(string Message, string? DevToken = null);

// ---- plumbing -------------------------------------------------------

/// <summary>Per-request metadata a provider records against issued tokens.</summary>
public readonly record struct AuthCallContext(string? Ip, string? UserAgent);

/// <summary>
/// Uniform provider result. On failure carries an RFC 7807-ish status/title/detail
/// the controller turns into a <c>ProblemDetails</c> response.
/// </summary>
public sealed class AuthResult<T>
{
    private AuthResult(bool ok, T? value, int status, string? title, string? detail)
    {
        Ok = ok;
        Value = value;
        Status = status;
        Title = title;
        Detail = detail;
    }

    public bool Ok { get; }

    public T? Value { get; }

    public int Status { get; }

    public string? Title { get; }

    public string? Detail { get; }

    public static AuthResult<T> Success(T value) => new(true, value, 200, null, null);

    public static AuthResult<T> Fail(int status, string title, string? detail = null) =>
        new(false, default, status, title, detail);
}

/// <summary>Marker for provider operations that return no body.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
