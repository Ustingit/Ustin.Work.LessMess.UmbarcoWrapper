using System.Net;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var options = new WrapperOptions
{
    UmbracoBaseUrl = builder.Configuration["Umbraco:BaseUrl"] ?? "http://localhost:8080",
    DataDirectory = builder.Configuration["Wrapper:DataDirectory"]
                    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"),
};
builder.Services.AddSingleton(options);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<ISessionStore, FileSessionStore>();
builder.Services.AddSingleton<IAuditLog, FileAuditLog>();
builder.Services.AddScoped<WrapperSession>();

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

WebApplication app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
