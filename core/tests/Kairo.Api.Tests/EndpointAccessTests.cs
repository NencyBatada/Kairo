using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Api.Tests;

public sealed class EndpointAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] Anonymous =
    [
        "GET /api/auth/sign-in",
        "POST /api/auth/sign-out",
        "GET /api/connections/callback/google",
        "* /health",
        "* /alive",
        "GET /openapi/{documentName}.json",
    ];

    [Fact]
    public void Only_the_allowed_endpoints_skip_sign_in()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var anonymous = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal);

        Assert.True(endpoints.Count > Anonymous.Length);
        Assert.Equal(Anonymous.Order(StringComparer.Ordinal), anonymous);
    }

    [Fact]
    public async Task Every_other_endpoint_needs_a_signed_in_user()
    {
        var fallback = await factory.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task A_signed_out_request_to_a_workspace_is_unauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/brands/{Guid.NewGuid()}/connections", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var method = methods is { Count: > 0 } ? string.Join(",", methods) : "*";
        return $"{method} /{endpoint.RoutePattern.RawText!.TrimStart('/')}";
    }
}
