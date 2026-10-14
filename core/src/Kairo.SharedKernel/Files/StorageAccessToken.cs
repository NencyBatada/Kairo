using Google.Apis.Auth.OAuth2;

namespace Kairo.SharedKernel.Files;

public interface IStorageAccessToken
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationDefaultStorageToken : IStorageAccessToken
{
    public const string Scope = "https://www.googleapis.com/auth/devstorage.read_write";

    private GoogleCredential? _credential;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)).CreateScoped(Scope);
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}
