using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth.Jwt;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Tests;

public sealed class JwtTokenServiceTests
{
    private static JwtTokenService Service(out JwtOptions options)
    {
        options = new JwtOptions
        {
            Issuer = "Ustin Provider Wrapper",
            Audience = "ustin-provider-wrapper",
            SigningKey = "unit-test-signing-key-must-be-at-least-32-bytes!!",
            AccessTokenLifetime = TimeSpan.FromHours(3),
        };
        return new JwtTokenService(Options.Create(options));
    }

    [Fact]
    public void Create_puts_expected_claims_and_3h_expiry()
    {
        JwtTokenService service = Service(out JwtOptions options);
        var key = Guid.NewGuid();

        AccessToken token = service.Create(key, "alice", "alice@example.com", true, "Alice", new[] { "member", "beta" });

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        Assert.Equal(key.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == "preferred_username").Value);
        Assert.Equal("true", jwt.Claims.Single(c => c.Type == "email_verified").Value);
        Assert.Equal(new[] { "member", "beta" }, jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToArray());
        Assert.Equal(options.Issuer, jwt.Issuer);
        Assert.Contains(options.Audience, jwt.Audiences);

        var lifetime = token.ExpiresAt - DateTimeOffset.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, 179, 181);
    }

    [Fact]
    public void Token_validates_against_the_same_key()
    {
        JwtTokenService service = Service(out JwtOptions options);
        AccessToken token = service.Create(Guid.NewGuid(), "bob", "bob@example.com", false, "Bob", Array.Empty<string>());

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = JwtTokenService.ResolveSigningKey(options),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        };

        new JwtSecurityTokenHandler().ValidateToken(token.Value, parameters, out SecurityToken validated);
        Assert.NotNull(validated);
    }

    [Fact]
    public void Short_key_is_rejected()
    {
        var bad = new JwtOptions { SigningKey = "too-short" };
        Assert.Throws<InvalidOperationException>(() => JwtTokenService.ResolveSigningKey(bad));
    }
}
