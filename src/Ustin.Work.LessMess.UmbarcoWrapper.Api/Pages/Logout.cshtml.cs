using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Pages;

public sealed class LogoutModel : PageModel
{
    private readonly UmbracoClient _umbraco;
    private readonly WrapperSession _session;
    private readonly IAuditLog _audit;

    public LogoutModel(UmbracoClient umbraco, WrapperSession session, IAuditLog audit)
    {
        _umbraco = umbraco;
        _session = session;
        _audit = audit;
    }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        SessionRecord? record = await _session.GetAsync();
        if (record is not null)
        {
            await _umbraco.LogoutAsync(record.Cookies);
            await _audit.WriteAsync(new AuditEntry(
                "logout", record.Username, _session.ClientIp, _session.UserAgent, "success",
                "umbraco sign-out called, wrapper session cleared"));
            await _session.EndAsync();
        }

        return RedirectToPage("/Login", new { message = "You have been signed out." });
    }
}
