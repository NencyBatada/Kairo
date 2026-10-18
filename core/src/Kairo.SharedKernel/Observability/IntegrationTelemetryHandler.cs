using System.Net;
using Microsoft.Extensions.Logging;

namespace Kairo.SharedKernel.Observability;

public sealed class IntegrationTelemetryHandler(ILogger<IntegrationTelemetryHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (request.RequestUri is { } uri && uri.Host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                KairoTelemetry.Event(logger, "integration.quota_exceeded");
            else if (!response.IsSuccessStatusCode)
                KairoTelemetry.Event(logger, "integration.failed");
        }
        return response;
    }
}
