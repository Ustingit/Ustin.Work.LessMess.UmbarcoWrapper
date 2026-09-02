using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Pages.V2;

public sealed class LogoutModel : PageModel
{
    private readonly BackofficeOAuthClient _oauth;
    private readonly BackofficeSession _session;
    private readonly IAuditLog _audit;

    public LogoutModel(BackofficeOAuthClient oauth, BackofficeSession session, IAuditLog audit)
    {
        _oauth = oauth;
        _session = session;
        _audit = audit;
    }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        BackofficeSessionRecord? record = await _session.GetValidAsync();
        if (record is not null)
        {
            await _oauth.LogoutAsync(record.Cookies);
            await _audit.WriteAsync(new AuditEntry(
                "backoffice.logout", record.Username, _session.ClientIp, _session.UserAgent, "success",
                "back-office token revoked, wrapper session cleared"));
            await _session.EndAsync();
        }

        return RedirectToPage("/V2/Login", new { message = "You have been signed out." });
    }
}
