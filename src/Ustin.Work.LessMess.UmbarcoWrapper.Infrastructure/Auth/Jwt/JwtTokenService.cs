using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth.Jwt;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Jwt;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, string TokenId);

/// <summary>Issues HS256 access tokens. Shared by both auth providers.</summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public static SymmetricSecurityKey ResolveSigningKey(JwtOptions options)
    {
        var raw = string.IsNullOrWhiteSpace(options.SigningKey)
            ? JwtOptions.DevelopmentSigningKeyFallback
            : options.SigningKey;

        var bytes = Encoding.UTF8.GetBytes(raw);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes for HS256.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    public AccessToken Create(
        Guid memberKey,
        string username,
        string email,
        bool emailVerified,
        string name,
        IEnumerable<string> roles)
    {
        DateTime now = DateTime.UtcNow;
        DateTime expires = now.Add(_options.AccessTokenLifetime);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new("sub", memberKey.ToString()),
            new("jti", jti),
            new("preferred_username", username),
            new("email", email),
            new("email_verified", emailVerified ? "true" : "false"),
            new("name", name),
        };
        claims.AddRange(roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => new Claim("role", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(
                ResolveSigningKey(_options), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
        return new AccessToken(token, new DateTimeOffset(expires, TimeSpan.Zero), jti);
    }
}
