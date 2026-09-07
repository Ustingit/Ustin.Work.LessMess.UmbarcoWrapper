using Microsoft.AspNetCore.Identity;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Auth.Local;

/// <summary>Local-mode member. <see cref="IdentityUser{TKey}.Id"/> is the member key.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string? Name { get; set; }
}
