using Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Tests;

public sealed class ClientRegistryTests
{
    private static ClientRegistry Registry() => ClientRegistry.ForTesting(
        new ClientCredential { Id = "mobile-app", Key = "k7f2-secret" },
        new ClientCredential { Id = "retired", Key = "old-key", Disabled = true });

    [Theory]
    [InlineData(null, "k7f2-secret", ClientCheck.MissingHeaders)]
    [InlineData("mobile-app", "", ClientCheck.MissingHeaders)]
    [InlineData("ghost", "whatever", ClientCheck.UnknownClient)]
    [InlineData("retired", "old-key", ClientCheck.Disabled)]
    [InlineData("mobile-app", "wrong", ClientCheck.BadKey)]
    [InlineData("mobile-app", "k7f2-secret", ClientCheck.Ok)]
    public void Verify_covers_each_outcome(string? id, string? key, ClientCheck expected) =>
        Assert.Equal(expected, Registry().Verify(id, key));

    [Fact]
    public void Client_id_is_case_insensitive()
    {
        Assert.Equal(ClientCheck.Ok, Registry().Verify("Mobile-App", "k7f2-secret"));
    }

    [Fact]
    public void Key_comparison_is_case_sensitive()
    {
        Assert.Equal(ClientCheck.BadKey, Registry().Verify("mobile-app", "K7F2-SECRET"));
    }
}
