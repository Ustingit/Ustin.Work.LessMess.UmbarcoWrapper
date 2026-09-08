using System.Net;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Swagger;
using Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.DependencyInjection;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var options = new WrapperOptions
{
    UmbracoBaseUrl = builder.Configuration["Umbraco:BaseUrl"] ?? "http://localhost:8080",
    UmbracoManagementBaseUrl = builder.Configuration["Umbraco:ManagementBaseUrl"],
    AllowInvalidCertificate = builder.Configuration.GetValue("Umbraco:AllowInvalidCertificate", false),
    DataDirectory = builder.Configuration["Wrapper:DataDirectory"]
                    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"),
};
builder.Services.AddSingleton(options);

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// Member auth API for the mobile app: Local (Postgres) or Proxy (upstream Umbraco).
builder.AddWrapperMemberAuth();
builder.Services.AddWrapperSwagger(builder.Configuration);

// Known-consumer gate: reject calls to /api/member-auth without a valid
// X-Client-Id / X-Client-Key (cheap filter + revocable kill-switch).
builder.Services.AddClientGate(builder.Configuration);

// Rate limiting: per client+IP windows on the sensitive endpoints + a global
// concurrency backstop protecting the DB / Umbraco.
builder.Services.AddWrapperRateLimiting(builder.Configuration);

builder.Services.AddSingleton<ISessionStore, FileSessionStore>();
builder.Services.AddSingleton<IAuditLog, FileAuditLog>();
builder.Services.AddScoped<WrapperSession>();

// v2 (back-office JWT) persistence + helper.
builder.Services.AddSingleton<IBackofficeSessionStore, BackofficeSessionStore>();
builder.Services.AddScoped<BackofficeSession>();

builder.Services
    .AddHttpClient<UmbracoClient>(http =>
    {
        http.BaseAddress = new Uri(options.UmbracoBaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(30);
    })
    // Don't let the handler manage cookies: we capture Set-Cookie ourselves and
    // replay it per wrapper session.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
    });

builder.Services
    .AddHttpClient<BackofficeOAuthClient>(http =>
    {
        http.BaseAddress = new Uri(options.ManagementBaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false };
        if (options.AllowInvalidCertificate)
        {
            // Local only: Umbraco is served with a self-signed dev cert over HTTPS.
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return handler;
    });

WebApplication app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.UseClientGate();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseWrapperSwagger();

app.MapRazorPages();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.MigrateWrapperMemberAuthDbAsync();

app.Run();
