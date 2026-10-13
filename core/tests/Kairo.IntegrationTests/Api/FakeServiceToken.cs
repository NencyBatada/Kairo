using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Api;

internal sealed class FakeServiceToken : IServiceAccessToken
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(FakeGoogleApis.ServiceToken);
}
