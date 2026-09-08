using Ustin.Work.LessMess.UmbarcoWrapper.Core.Auditing;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Pages;

public sealed class RegisterModel : PageModel
{
    private readonly UmbracoClient _umbraco;
    private readonly WrapperSession _session;
    private readonly IAuditLog _audit;

    public RegisterModel(UmbracoClient umbraco, WrapperSession session, IAuditLog audit)
    {
        _umbraco = umbraco;
        _session = session;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Error { get; private set; }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Fill in every field with a valid value.";
            return Page();
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            Error = "Passwords do not match.";
            return Page();
        }

        AuthResult result = await _umbraco.RegisterAsync(Input.Username, Input.Email, Input.Password);
        if (!result.Ok)
        {
            await _audit.WriteAsync(new AuditEntry(
                "register.failed", Input.Username, _session.ClientIp, _session.UserAgent, "failure", result.Error));
            Error = result.Error ?? "Registration failed.";
            return Page();
        }

        await _audit.WriteAsync(new AuditEntry(
            "register", Input.Username, _session.ClientIp, _session.UserAgent, "success",
            "proxied to umbraco, member created"));

        return RedirectToPage("/Login", new
        {
            username = Input.Username,
            message = "Account created. Sign in to continue.",
        });
    }
}
