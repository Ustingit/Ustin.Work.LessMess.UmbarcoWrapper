using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddWrapperRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<RateLimitingOptions>().Bind(config.GetSection(RateLimitingOptions.SectionName));
        var opt = config.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = OnRejectedAsync;

            if (!opt.Enabled)
            {
                return;
            }

            // Backstop: cap total in-flight requests so a flood can't overwhelm the DB / Umbraco.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = opt.GlobalMaxConcurrent,
                    QueueLimit = opt.GlobalQueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));

            // Fixed windows so a Retry-After header is always emitted on 429.
            Fixed(limiter, RateLimitPolicies.Login, opt.Login, ClientAndIp);
            Fixed(limiter, RateLimitPolicies.Refresh, opt.Refresh, ClientAndIp);
            Fixed(limiter, RateLimitPolicies.PasswordForgot, opt.PasswordForgot, Ip);
            Fixed(limiter, RateLimitPolicies.Register, opt.Register, Ip);
        });

        return services;
    }

    private static void Fixed(RateLimiterOptions limiter, string name, WindowPolicy p, Func<HttpContext, string> key) =>
        limiter.AddPolicy(name, ctx => RateLimitPartition.GetFixedWindowLimiter(
            key(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = p.PermitLimit,
                Window = TimeSpan.FromSeconds(p.WindowSeconds),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

    private static string ClientAndIp(HttpContext ctx) =>
        (ctx.Items[ClientGateMiddleware.ClientIdItemKey] as string ?? "anon")
        + "|" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "no-ip");

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "no-ip";

    private static async ValueTask OnRejectedAsync(OnRejectedContext ctx, CancellationToken ct)
    {
        HttpContext http = ctx.HttpContext;

        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            http.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter")
            .LogWarning(
                "rate-limit: {Method} {Path} from {Ip} (client {ClientId})",
                http.Request.Method,
                http.Request.Path,
                http.Connection.RemoteIpAddress,
                http.Items[ClientGateMiddleware.ClientIdItemKey] ?? "(none)");

        http.Response.ContentType = "application/problem+json";
        await http.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
                title = "Too many requests",
                status = 429,
                detail = "Rate limit exceeded. Retry after the period in the Retry-After header.",
            },
            ct);
    }
}
