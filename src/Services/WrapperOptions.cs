namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

public sealed class WrapperOptions
{
    /// <summary>Base URL of the Umbraco instance, e.g. http://umbraco:8080 (v1: member cookie proxy).</summary>
    public string UmbracoBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    ///     Base URL used for the v2 back-office OAuth flow + Management API. Umbraco's
    ///     OpenIddict server requires HTTPS, so this is usually the https:// address.
    ///     Falls back to <see cref="UmbracoBaseUrl"/> when unset.
    /// </summary>
    public string? UmbracoManagementBaseUrl { get; set; }

    /// <summary>Trust a self-signed Umbraco TLS cert (local/dev only).</summary>
    public bool AllowInvalidCertificate { get; set; }

    /// <summary>Directory for the persistent layer (session maps + audit log).</summary>
    public string DataDirectory { get; set; } = "App_Data";

    public string ManagementBaseUrl => string.IsNullOrWhiteSpace(UmbracoManagementBaseUrl)
        ? UmbracoBaseUrl
        : UmbracoManagementBaseUrl;
}
