using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;

namespace Kairo.SharedKernel.Agents;

public interface IAgentIdentity
{
    Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken);
}

internal sealed class GoogleAgentIdentity : IAgentIdentity
{
    private readonly ConcurrentDictionary<string, Task<OidcToken>> _tokens = new();

    public async Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken)
    {
        var pending = _tokens.GetOrAdd(audience, CreateAsync);
        try
        {
            var token = await pending;
            return await token.GetAccessTokenAsync(cancellationToken);
        }
        catch
        {
            _tokens.TryRemove(new KeyValuePair<string, Task<OidcToken>>(audience, pending));
            throw;
        }
    }

    private static async Task<OidcToken> CreateAsync(string audience)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync();
        return await credential.GetOidcTokenAsync(OidcTokenOptions.FromTargetAudience(audience));
    }
}
