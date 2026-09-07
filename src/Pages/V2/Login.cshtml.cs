using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Pages.V2;

public sealed class LoginModel : PageModel
{
    private readonly BackofficeOAuthClient _oauth;
    private readonly BackofficeSession _session;
    private readonly IAuditLog _audit;

    public LoginModel(BackofficeOAuthClient oauth, BackofficeSession session, IAuditLog audit)
    {
        _oauth = oauth;
        _session = session;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Error { get; private set; }

    public string? Message { get; private set; }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet(string? message) => Message = message;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Enter a username and password.";
            return Page();
        }

        BackofficeLoginResult result = await _oauth.LoginAsync(Input.Username, Input.Password);
        if (!result.Ok || result.Session is null)
        {
            await _audit.WriteAsync(new AuditEntry(
                "backoffice.login.failed", Input.Username, _session.ClientIp, _session.UserAgent, "failure", result.Error));
            Error = result.TwoFactorRequired
                ? "This user has two-factor auth enabled; the headless flow can't complete it."
                : result.Error ?? "Sign in failed.";
            return Page();
        }

        await _session.StartAsync(Input.Username, result.Session);
        await _audit.WriteAsync(new AuditEntry(
            "backoffice.login", Input.Username, _session.ClientIp, _session.UserAgent, "success",
            $"umbraco back-office BFF cookie set stored, access expires {result.Session.ExpiresAtUtc:o}"));

        return RedirectToPage("/V2/Translations");
    }
}
