using Microsoft.AspNetCore.Identity;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Infrastructure.LocalAuthRepository;

/// <summary>Local-mode member. <see cref="IdentityUser{TKey}.Id"/> is the member key.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string? Name { get; set; }
}
