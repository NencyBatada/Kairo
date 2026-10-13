using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Connections.Accounts;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionSetup
{
    public static void AddConnections(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(GoogleHttp.ClientName);
        services.AddSingleton<ConsentStates>();
        services.AddSingleton<GoogleOAuth>();
        var keyName = configuration["Kairo:Kms:KeyName"];
        if (string.IsNullOrEmpty(keyName))
        {
            services.AddSingleton<ITokenCipher, DataProtectionTokenCipher>();
        }
        else
        {
            services.AddSingleton<ITokenCipher>(new KmsTokenCipher(keyName));
        }

        services.AddSingleton<AccessTokenCache>();
        services.AddSingleton<IProductAccounts, GoogleAdsAccounts>();
        services.AddSingleton<IProductAccounts, YouTubeAccounts>();
        services.AddSingleton<IProductAccounts, MerchantCenterAccounts>();
        services.AddSingleton<IProductAccounts, Ga4Accounts>();
        services.AddSingleton<IProductAccounts, BigQueryAccounts>();
        services.AddSingleton<ProductCatalog>();
        services.AddScoped<ConnectionTokens>();
        services.AddScoped<GoogleRevocations>();
        services.AddScoped<IConnections, BrandConnections>();
    }

    public static void MapConnections(this IEndpointRouteBuilder api)
    {
        api.MapGet("/connections/callback/google", ConsentEndpoints.CallbackAsync);

        var connections = api.MapBrandApi().MapGroup("/connections");
        connections.MapGet("", ConnectionEndpoints.ListAsync);

        var product = connections.MapGroup("/{product}").RequireBrandRole(BrandRoles.Admin);
        product.MapGet("/connect", ConsentEndpoints.Connect);
        product.MapGet("/accounts", AccountEndpoints.ListAsync);
        product.MapPost("/account", AccountEndpoints.ChooseAsync);
        product.MapPost("/check", ConnectionEndpoints.CheckAsync);
        product.MapDelete("", ConnectionEndpoints.DisconnectAsync);
    }
}
