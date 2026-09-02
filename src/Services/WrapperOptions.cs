namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

public sealed class WrapperOptions
{
    /// <summary>Base URL of the Umbraco instance, e.g. http://umbraco:8080.</summary>
    public string UmbracoBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Directory for the persistent layer (session map + audit log).</summary>
    public string DataDirectory { get; set; } = "App_Data";
}
