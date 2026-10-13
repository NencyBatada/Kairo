using System.Collections.Concurrent;

namespace Kairo.Modules.Brands.Connections;

internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

internal sealed class AccessTokenCache(TimeProvider time)
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, CachedToken> _tokens = new();

    public AccessToken? Find(Connection connection) =>
        _tokens.TryGetValue(connection.Id, out var cached)
        && cached.ConnectedAt == connection.ConnectedAt
        && cached.Token.ExpiresAt - Margin > time.GetUtcNow()
            ? cached.Token
            : null;

    public void Store(Connection connection, AccessToken token) => _tokens[connection.Id] = new CachedToken(connection.ConnectedAt, token);

    public void Forget(Guid connectionId) => _tokens.TryRemove(connectionId, out _);

    private sealed record CachedToken(DateTimeOffset ConnectedAt, AccessToken Token);
}
