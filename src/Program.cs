using System.Net;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

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
builder.Services.AddHttpContextAccessor();

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
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
