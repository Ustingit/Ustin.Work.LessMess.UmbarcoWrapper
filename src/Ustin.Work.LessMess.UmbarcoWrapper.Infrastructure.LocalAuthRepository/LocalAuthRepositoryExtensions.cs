using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.LocalAuthRepository;

/// <summary>
/// Database B — registered and migrated ONLY in Local mode. Callers gate these
/// with <c>MemberAuth:Mode == Local</c>; nothing else in the app references it.
/// </summary>
public static class LocalAuthRepositoryExtensions
{
    public const string ConnectionStringName = "LocalAuthDb";

    public static TBuilder AddLocalAuthRepository<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var cs = builder.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required when MemberAuth:Mode=Local.");

        builder.Services.AddDbContext<LocalAuthDbContext>(o => o.UseNpgsql(cs));

        builder.Services.AddIdentityCore<AppUser>(o =>
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

        return builder;
    }

    public static async Task MigrateLocalAuthRepositoryAsync(this IHost host)
    {
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LocalAuthDbMigrator");
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        LocalAuthDbContext db = scope.ServiceProvider.GetRequiredService<LocalAuthDbContext>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("LocalAuthDb migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "LocalAuthDb migrate attempt {Attempt} failed; retrying in 3s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
