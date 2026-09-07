using System.Security.Cryptography;
using System.Text;
using Ustin.Work.LessMess.UmbarcoWrapper.Api.Services;
using Xunit;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Tests;

public sealed class PkceTests
{
    [Fact]
    public void Challenge_is_base64url_sha256_of_verifier()
    {
        (var verifier, var challenge) = Pkce.Create();

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, challenge);
    }

    [Fact]
    public void Verifier_length_is_within_rfc7636_bounds_and_url_safe()
    {
        (var verifier, _) = Pkce.Create();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.DoesNotContain('=', verifier);
    }

    [Fact]
    public void Each_call_is_unique()
    {
        (var v1, var c1) = Pkce.Create();
        (var v2, var c2) = Pkce.Create();

        Assert.NotEqual(v1, v2);
        Assert.NotEqual(c1, c2);
        Assert.NotEqual(Pkce.RandomState(), Pkce.RandomState());
    }
}
