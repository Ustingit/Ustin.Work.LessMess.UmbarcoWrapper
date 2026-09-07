using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth.Jwt;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth.Local;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth.Proxy;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth;

public static class MemberAuthServiceCollectionExtensions
{
    public const string SchemeName = "member-jwt";

    public static WebApplicationBuilder AddMemberAuth(this WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;
        IConfiguration config = builder.Configuration;

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
            var cs = config.GetConnectionString("LocalAuthDb")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:LocalAuthDb is required when MemberAuth:Mode=Local.");

            services.AddDbContext<LocalAuthDbContext>(o => o.UseNpgsql(cs));

            services.AddIdentityCore<AppUser>(o =>
                {
                    o.Password.RequiredLength = 10;
                    o.Password.RequireNonAlphanumeric = false;
                    o.Password.RequireDigit = false;
                    o.Password.RequireUppercase = false;
                    o.Password.RequireLowercase = false;
                    o.User.RequireUniqueEmail = true;
                    o.Lockout.MaxFailedAccessAttempts = 5;
                })
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<LocalAuthDbContext>()
                .AddDefaultTokenProviders();

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

    /// <summary>Applies EF migrations for the local store (Local mode only), with a short retry.</summary>
    public static async Task MigrateLocalAuthDbAsync(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<MemberAuthOptions>>().Value.Mode != AuthMode.Local)
        {
            return;
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        LocalAuthDbContext db = scope.ServiceProvider.GetRequiredService<LocalAuthDbContext>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                app.Logger.LogInformation("LocalAuthDb migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                app.Logger.LogWarning(ex, "LocalAuthDb migrate attempt {Attempt} failed; retrying in 3s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
