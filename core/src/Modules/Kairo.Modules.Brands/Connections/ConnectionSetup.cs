namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionSetup
{
    public static void AddConnections(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(GoogleHttp.ClientName);
        services.AddSingleton<ConsentStates>();
        services.AddSingleton<GoogleOAuth>();
        services.AddSingleton<ITokenCipher, DataProtectionTokenCipher>();
        services.AddScoped<ConnectionTokens>();
        services.AddScoped<GoogleRevocations>();
    }

    public static void MapConnections(this IEndpointRouteBuilder api)
    {
        api.MapGet("/connections/callback/google", ConsentEndpoints.CallbackAsync);

        var connections = api.MapBrandApi().MapGroup("/connections");
        connections.MapGet("", ConnectionEndpoints.ListAsync);

        var product = connections.MapGroup("/{product}").RequireBrandRole(BrandRoles.Admin);
        product.MapGet("/connect", ConsentEndpoints.Connect);
    }
}
