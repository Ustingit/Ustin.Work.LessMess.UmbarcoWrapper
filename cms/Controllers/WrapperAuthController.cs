using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Cms.Controllers;

/// <summary>
///     The auth surface the wrapper proxies to. Everything here is plain ASP.NET Core
///     that delegates to Umbraco's own member identity services.
/// </summary>
[ApiController]
[Route("api/wrapper/auth")]
[Produces("application/json")]
public sealed class WrapperAuthController : ControllerBase
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberSignInManager _signInManager;
    private readonly ILogger<WrapperAuthController> _logger;

    public WrapperAuthController(
        IMemberManager memberManager,
        IMemberSignInManager signInManager,
        ILogger<WrapperAuthController> logger)
    {
        _memberManager = memberManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public sealed record RegisterRequest(string Username, string Email, string Password);

    public sealed record LoginRequest(string Username, string Password);

    public sealed record MemberResponse(string Username, string Email, string Name);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "username, email and password are required" });
        }

        MemberIdentityUser identityUser = MemberIdentityUser.CreateNew(
            request.Username,
            request.Email,
            Constants.Security.DefaultMemberTypeAlias,
            isApproved: true,
            request.Username);

        IdentityResult result = await _memberManager.CreateAsync(identityUser, request.Password);
        if (!result.Succeeded)
        {
            var reason = string.Join("; ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("wrapper-api: registration rejected for {Username}: {Reason}", request.Username, reason);
            return BadRequest(new { error = reason });
        }

        _logger.LogInformation("wrapper-api: member registered {Username}", request.Username);
        return Ok(new MemberResponse(identityUser.UserName!, identityUser.Email!, identityUser.Name ?? request.Username));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        Microsoft.AspNetCore.Identity.SignInResult result = await _signInManager.PasswordSignInAsync(
            request.Username,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            _logger.LogWarning("wrapper-api: login failed for {Username}", request.Username);
            return Unauthorized(new { error = "invalid username or password" });
        }

        MemberIdentityUser? member = await _memberManager.FindByNameAsync(request.Username);
        _logger.LogInformation("wrapper-api: member logged in {Username}", request.Username);

        // The member auth cookie is written to the response by PasswordSignInAsync.
        return Ok(new MemberResponse(
            member?.UserName ?? request.Username,
            member?.Email ?? string.Empty,
            member?.Name ?? request.Username));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        MemberIdentityUser? member = await _memberManager.GetCurrentMemberAsync();
        await _signInManager.SignOutAsync();
        _logger.LogInformation("wrapper-api: member logged out {Username}", member?.UserName ?? "(unknown)");
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        MemberIdentityUser? member = await _memberManager.GetCurrentMemberAsync();
        if (member is null)
        {
            return Unauthorized(new { error = "not authenticated" });
        }

        return Ok(new MemberResponse(member.UserName!, member.Email ?? string.Empty, member.Name ?? member.UserName!));
    }
}
