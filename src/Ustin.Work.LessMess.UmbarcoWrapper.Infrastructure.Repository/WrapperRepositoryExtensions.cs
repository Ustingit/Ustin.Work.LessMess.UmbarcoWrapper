using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auditing;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.Repository;

/// <summary>Database A — durable, registered and migrated in every mode.</summary>
public static class WrapperRepositoryExtensions
{
    public const string ConnectionStringName = "WrapperDb";

    public static TBuilder AddWrapperRepository<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var cs = builder.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required (the durable store exists in every mode).");

        builder.Services.AddDbContext<WrapperDbContext>(o => o.UseNpgsql(cs));
        builder.Services.AddScoped<IAuditLog, PostgresAuditLog>();

        return builder;
    }

    public static async Task MigrateWrapperRepositoryAsync(this IHost host)
    {
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WrapperDbMigrator");
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        WrapperDbContext db = scope.ServiceProvider.GetRequiredService<WrapperDbContext>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("WrapperDb migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "WrapperDb migrate attempt {Attempt} failed; retrying in 3s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
