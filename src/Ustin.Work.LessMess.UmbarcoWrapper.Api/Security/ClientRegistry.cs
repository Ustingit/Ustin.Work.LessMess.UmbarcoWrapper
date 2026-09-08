using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;

public enum ClientCheck
{
    Ok,
    MissingHeaders,
    UnknownClient,
    Disabled,
    BadKey,
}

/// <summary>
/// Looks up a configured client by id and compares its key in fixed time.
/// Pure and unit-testable; the middleware is a thin wrapper over it.
/// </summary>
public sealed class ClientRegistry
{
    private readonly IReadOnlyDictionary<string, ClientCredential> _byId;

    public ClientRegistry(IOptions<ClientGateOptions> options) => _byId = Index(options.Value.Clients);

    private ClientRegistry(IEnumerable<ClientCredential> clients) => _byId = Index(clients);

    /// <summary>Test helper — the DI constructor is the only public one.</summary>
    public static ClientRegistry ForTesting(params ClientCredential[] clients) => new(clients);

    public ClientCheck Verify(string? clientId, string? clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientKey))
        {
            return ClientCheck.MissingHeaders;
        }

        if (!_byId.TryGetValue(clientId, out ClientCredential? client))
        {
            return ClientCheck.UnknownClient;
        }

        if (client.Disabled)
        {
            return ClientCheck.Disabled;
        }

        return CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(client.Key),
                   Encoding.UTF8.GetBytes(clientKey))
            ? ClientCheck.Ok
            : ClientCheck.BadKey;
    }

    private static IReadOnlyDictionary<string, ClientCredential> Index(IEnumerable<ClientCredential> clients) =>
        clients
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
}
