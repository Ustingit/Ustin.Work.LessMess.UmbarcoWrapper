namespace Ustin.Work.LessMess.UmbarcoWrapper.Core.Auth;

public enum AuthMode
{
    /// <summary>Self-contained identity store on a local Postgres database.</summary>
    Local,

    /// <summary>Forward every call to a real Umbraco instance.</summary>
    Proxy,
}

/// <summary>Bound from the <c>MemberAuth</c> configuration section.</summary>
public sealed class MemberAuthOptions
{
    public const string SectionName = "MemberAuth";

    public AuthMode Mode { get; set; } = AuthMode.Local;

    /// <summary>Proxy mode: base URL of the upstream Umbraco member-auth API.</summary>
    public string UpstreamBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Refuse to boot in Production with <see cref="AuthMode.Local"/> unless set.</summary>
    public bool AllowLocalInProduction { get; set; }

    public bool RequireEmailConfirmation { get; set; }

    public bool AutoLoginAfterRegister { get; set; } = true;

    /// <summary>Dev aid: return reset / confirmation tokens in responses (no SMTP locally).</summary>
    public bool ExposeTokensInResponses { get; set; }

    /// <summary>Proxy mode token-validation settings (the wrapper validates the tokens it relays).</summary>
    public ProxySettings Proxy { get; set; } = new();

    public sealed class ProxySettings
    {
        public string Issuer { get; set; } = "Umbraco";

        public string Audience { get; set; } = "umbraco-members";

        /// <summary>Must match the upstream instance's HS256 member-JWT signing key.</summary>
        public string SharedSigningKey { get; set; } = string.Empty;
    }
}
