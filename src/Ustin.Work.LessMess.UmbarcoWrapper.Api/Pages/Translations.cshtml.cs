using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Pages;

public sealed class TranslationsModel : PageModel
{
    private readonly UmbracoClient _umbraco;
    private readonly WrapperSession _session;
    private readonly IAuditLog _audit;

    public TranslationsModel(UmbracoClient umbraco, WrapperSession session, IAuditLog audit)
    {
        _umbraco = umbraco;
        _session = session;
        _audit = audit;
    }

    public string Username { get; private set; } = string.Empty;

    public TranslationsResult? Data { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        SessionRecord? record = await _session.GetAsync();
        if (record is null)
        {
            return RedirectToPage("/Login");
        }

        Username = record.Username;
        TranslationsResponse response = await _umbraco.GetTranslationsAsync(record.Cookies);

        if (response.Unauthorized)
        {
            await _audit.WriteAsync(new AuditEntry(
                "translations.list", record.Username, _session.ClientIp, _session.UserAgent, "session-expired",
                "umbraco rejected the stored member cookie"));
            await _session.EndAsync();
            return RedirectToPage("/Login", new { message = "Your session expired. Sign in again." });
        }

        if (!response.Ok || response.Data is null)
        {
            await _audit.WriteAsync(new AuditEntry(
                "translations.list", record.Username, _session.ClientIp, _session.UserAgent, "failure", response.Error));
            return StatusCode(StatusCodes.Status502BadGateway, response.Error ?? "Umbraco call failed");
        }

        Data = response.Data;
        await _session.TouchAsync();
        await _audit.WriteAsync(new AuditEntry(
            "translations.list", record.Username, _session.ClientIp, _session.UserAgent, "success",
            $"{response.Data.Count} items"));

        return Page();
    }
}
