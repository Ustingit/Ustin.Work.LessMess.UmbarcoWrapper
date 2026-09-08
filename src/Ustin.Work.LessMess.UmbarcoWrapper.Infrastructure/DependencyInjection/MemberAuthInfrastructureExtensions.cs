using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth.Jwt;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Jwt;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Auth.Proxy;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.LocalAuthRepository;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the member-auth stack. Host-agnostic: the same call works from the
/// API's <see cref="WebApplicationBuilder"/> and from the Worker's generic host
/// builder (both are <see cref="IHostApplicationBuilder"/>).
///
/// Database A (<see cref="WrapperDbContext"/>) is registered unconditionally;
/// database B (<see cref="LocalAuthDbContext"/>) only when <c>MemberAuth:Mode=Local</c>.
/// </summary>
public static class MemberAuthInfrastructureExtensions
{
    public const string SchemeName = "member-jwt";

    public static TBuilder AddWrapperMemberAuth<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        IServiceCollection services = builder.Services;
        IConfiguration config = builder.Configuration;

        // Database A — durable, every mode.
        builder.AddWrapperRepository();

        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.SectionName));
        services.AddOptions<MemberAuthOptions>().Bind(config.GetSection(MemberAuthOptions.SectionName));

        var memberAuth = config.GetSection(MemberAuthOptions.SectionName).Get<MemberAuthOptions>() ?? new MemberAuthOptions();
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (memberAuth.Mode == AuthMode.Local
            && builder.Environment.IsProduction()
            && !memberAuth.AllowLocalInProduction)
        {
            throw new InvalidOperationException(
                "MemberAuth:Mode=Local is blocked in Production. Set MemberAuth:AllowLocalInProduction=true to override.");
        }

        services.AddSingleton<JwtTokenService>();
        services.AddHttpContextAccessor();

        // The wrapper validates the tokens it hands out (Local) or the ones it relays (Proxy).
        (string issuer, string audience, SecurityKey key) = memberAuth.Mode == AuthMode.Local
            ? (jwt.Issuer, jwt.Audience, JwtTokenService.ResolveSigningKey(jwt))
            : (memberAuth.Proxy.Issuer,
               memberAuth.Proxy.Audience,
               new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                   string.IsNullOrWhiteSpace(memberAuth.Proxy.SharedSigningKey)
                       ? JwtOptions.DevelopmentSigningKeyFallback
                       : memberAuth.Proxy.SharedSigningKey)));

        services.AddAuthentication()
            .AddJwtBearer(SchemeName, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = jwt.ClockSkew,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role",
                };
            });
        services.AddAuthorization();

        if (memberAuth.Mode == AuthMode.Local)
        {
            // Database B — only in Local mode.
            builder.AddLocalAuthRepository();
            services.AddScoped<IMemberAuthProvider, LocalMemberAuthProvider>();
        }
        else
        {
            services.AddHttpClient<IMemberAuthProvider, ProxyMemberAuthProvider>(c =>
            {
                c.BaseAddress = new Uri(memberAuth.UpstreamBaseUrl.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(30);
            });
        }

        return builder;
    }

    /// <summary>
    /// Migrates database A always; database B only in Local mode. Short retry loop
    /// because compose brings Postgres up in parallel.
    /// </summary>
    public static async Task MigrateWrapperMemberAuthDbAsync(this IHost host)
    {
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MemberAuth");
        var memberAuth = host.Services.GetRequiredService<IOptions<MemberAuthOptions>>().Value;

        await host.MigrateWrapperRepositoryAsync();

        if (memberAuth.Mode == AuthMode.Local)
        {
            await host.MigrateLocalAuthRepositoryAsync();
            logger.LogInformation(
                "MemberAuth mode=Local; databases: WrapperDb (durable, A) + LocalAuthDb (identity store, B)");
        }
        else
        {
            logger.LogInformation(
                "MemberAuth mode=Proxy; databases: WrapperDb (durable, A) only. "
                + "LocalAuthDb is not registered, migrated or read in this mode - "
                + "drop the localauth database if this instance was previously in Local mode.");

            if (string.IsNullOrWhiteSpace(memberAuth.Proxy.SharedSigningKey))
            {
                logger.LogWarning(
                    "MemberAuth:Proxy:SharedSigningKey is empty - relayed tokens are validated with the "
                    + "well-known development key. Set it to the upstream member-JWT signing key.");
            }
        }
    }
}
