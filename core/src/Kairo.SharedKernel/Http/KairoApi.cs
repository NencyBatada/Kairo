using Kairo.SharedKernel.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kairo.SharedKernel.Http;

public static class KairoApi
{
    public const string RequestHeader = "X-Kairo-Request";

    public static RouteGroupBuilder MapKairoApi(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api")
            .AddEndpointFilter<RequestHeaderFilter>()
            .AddEndpointFilter<TransactionFilter>();

    public static RouteGroupBuilder MapBrandApi(this IEndpointRouteBuilder api) =>
        api.MapGroup("/brands/{brandId:guid}")
            .RequireAuthorization()
            .AddEndpointFilter<BrandAccessFilter>();

    public static TBuilder RequireBrandRole<TBuilder>(this TBuilder builder, BrandRoles roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new BrandRoleRequirement(roles));

    public static TBuilder WithoutRequestTransaction<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new NoRequestTransaction());
}

public sealed record BrandRoleRequirement(BrandRoles Roles);

public sealed record NoRequestTransaction;
