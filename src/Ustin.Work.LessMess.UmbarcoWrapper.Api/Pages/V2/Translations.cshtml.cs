using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Pages.V2;

public sealed class TranslationsModel : PageModel
{
    private readonly BackofficeOAuthClient _oauth;
    private readonly BackofficeSession _session;
    private readonly IAuditLog _audit;

    public TranslationsModel(BackofficeOAuthClient oauth, BackofficeSession session, IAuditLog audit)
    {
        _oauth = oauth;
        _session = session;
        _audit = audit;
    }

    public string Username { get; private set; } = string.Empty;

    public TranslationsResult? Data { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        BackofficeSessionRecord? record = await _session.GetValidAsync();
        if (record is null)
        {
            return RedirectToPage("/V2/Login");
        }

        Username = record.Username;
        BackofficeTranslationsResponse response = await _oauth.GetTranslationsAsync(record.Cookies);

        if (response.Unauthorized)
        {
            await _audit.WriteAsync(new AuditEntry(
                "backoffice.translations.list", record.Username, _session.ClientIp, _session.UserAgent, "session-expired",
                "umbraco rejected the access token"));
            await _session.EndAsync();
            return RedirectToPage("/V2/Login", new { message = "Your session expired. Sign in again." });
        }

        if (!response.Ok || response.Data is null)
        {
            await _audit.WriteAsync(new AuditEntry(
                "backoffice.translations.list", record.Username, _session.ClientIp, _session.UserAgent, "failure", response.Error));
            return StatusCode(StatusCodes.Status502BadGateway, response.Error ?? "Management API call failed");
        }

        Data = response.Data;
        await _audit.WriteAsync(new AuditEntry(
            "backoffice.translations.list", record.Username, _session.ClientIp, _session.UserAgent, "success",
            $"{response.Data.Count} items via management api"));

        return Page();
    }
}
