using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auditing;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Pages;

public sealed class LoginModel : PageModel
{
    private readonly UmbracoClient _umbraco;
    private readonly WrapperSession _session;
    private readonly IAuditLog _audit;

    public LoginModel(UmbracoClient umbraco, WrapperSession session, IAuditLog audit)
    {
        _umbraco = umbraco;
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

    public void OnGet(string? username, string? message)
    {
        Input.Username = username ?? string.Empty;
        Message = message;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Enter a username and password.";
            return Page();
        }

        AuthResult result = await _umbraco.LoginAsync(Input.Username, Input.Password);
        if (!result.Ok || result.Cookies is null)
        {
            await _audit.WriteAsync(new AuditEntry(
                "login.failed", Input.Username, _session.ClientIp, _session.UserAgent, "failure", result.Error));
            Error = result.Error ?? "Invalid username or password.";
            return Page();
        }

        await _session.StartAsync(result.Member?.Username ?? Input.Username, result.Member?.Email ?? string.Empty, result.Cookies);
        await _audit.WriteAsync(new AuditEntry(
            "login", result.Member?.Username ?? Input.Username, _session.ClientIp, _session.UserAgent, "success",
            "umbraco member cookie stored"));

        return RedirectToPage("/Translations");
    }
}
