using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Triggers.Weather;

namespace Kairo.IntegrationTests.Api;

internal sealed class FakeServiceToken : IServiceAccessToken
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(FakeGoogleApis.ServiceToken);
}

internal sealed class FakeWeatherToken : IWeatherToken
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(FakeGoogleApis.ServiceToken);
}
