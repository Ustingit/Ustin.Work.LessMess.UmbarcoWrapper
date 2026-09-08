namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;

/// <summary>Bound from the <c>ClientGate</c> configuration section.</summary>
public sealed class ClientGateOptions
{
    public const string SectionName = "ClientGate";

    /// <summary>Turn the gate off entirely (e.g. behind a mesh that already authenticates callers).</summary>
    public bool Enabled { get; set; } = true;

    public string ClientIdHeader { get; set; } = "X-Client-Id";

    public string ClientKeyHeader { get; set; } = "X-Client-Key";

    /// <summary>Request path prefixes the gate protects. Everything else passes through.</summary>
    public string[] ProtectedPathPrefixes { get; set; } = { "/api/member-auth" };

    public ClientCredential[] Clients { get; set; } = Array.Empty<ClientCredential>();
}

public sealed class ClientCredential
{
    public string Id { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>Revoke a leaked client without removing the row.</summary>
    public bool Disabled { get; set; }
}
