using Google.Apis.Auth.OAuth2;

namespace Kairo.Modules.Measurement.Feeds;

internal interface IServiceAccessToken
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationDefaultAccessToken : IServiceAccessToken
{
    public const string Scope = "https://www.googleapis.com/auth/bigquery";

    private GoogleCredential? _credential;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)).CreateScoped(Scope);
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}
