using System.Security.Cryptography;
using System.Text;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

/// <summary>RFC 7636 PKCE pair generation (S256).</summary>
public static class Pkce
{
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string RandomState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
