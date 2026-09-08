using Microsoft.Extensions.Options;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;

/// <summary>
/// Rejects calls to the protected paths that don't present a known
/// <c>X-Client-Id</c> / <c>X-Client-Key</c> pair, before they reach the
/// controller / Identity / the database.
///
/// This is a cheap filter and a revocable kill-switch — it is NOT a trust
/// boundary: the key ships inside the mobile app and can be read from a proxied
/// device. Pair it with the rate limiter and an edge WAF.
/// </summary>
public sealed class ClientGateMiddleware
{
    /// <summary><c>HttpContext.Items</c> key holding the verified client id (for logging / rate-limit partitioning).</summary>
    public const string ClientIdItemKey = "ClientId";

    private readonly RequestDelegate _next;
    private readonly ClientGateOptions _options;
    private readonly ClientRegistry _registry;
    private readonly ILogger<ClientGateMiddleware> _logger;

    public ClientGateMiddleware(
        RequestDelegate next,
        IOptions<ClientGateOptions> options,
        ClientRegistry registry,
        ILogger<ClientGateMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _registry = registry;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !IsProtected(context.Request.Path))
        {
            await _next(context);
            return;
        }

        string? id = context.Request.Headers[_options.ClientIdHeader];
        string? key = context.Request.Headers[_options.ClientKeyHeader];

        ClientCheck result = _registry.Verify(id, key);
        if (result == ClientCheck.Ok)
        {
            context.Items[ClientIdItemKey] = id;
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "client-gate: rejected {Method} {Path} from {Ip} — {Reason} (client id: {ClientId})",
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress,
            result,
            string.IsNullOrEmpty(id) ? "(none)" : id);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            title = "Unknown client",
            status = 401,
            detail = $"A valid {_options.ClientIdHeader} / {_options.ClientKeyHeader} pair is required.",
        });
    }

    private bool IsProtected(PathString path) =>
        _options.ProtectedPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
