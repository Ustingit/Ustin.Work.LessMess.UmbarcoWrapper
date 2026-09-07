namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth.Jwt;

/// <summary>Bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Ustin Provider Wrapper";

    public string Audience { get; set; } = "ustin-provider-wrapper";

    /// <summary>HS256 signing key, >= 32 bytes. Set from a secret in real environments.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access-token lifetime. Default 3 hours.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(3);

    /// <summary>Refresh-token absolute lifetime. Default 14 days.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    internal const string DevelopmentSigningKeyFallback =
        "ustin-provider-wrapper-development-signing-key-change-me";
}
