namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Local;

/// <summary>
/// A rotated refresh token. Only the SHA-256 hash of the raw value is stored.
/// </summary>
public sealed class LocalRefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }
}
