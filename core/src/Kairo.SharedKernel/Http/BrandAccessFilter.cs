using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;

namespace Kairo.SharedKernel.Http;

internal sealed class BrandAccessFilter(IBrandAccess access, KairoDatabase database, CurrentMember member) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.FindUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!Guid.TryParse(http.Request.RouteValues["brandId"]?.ToString(), out var brandId))
        {
            return TypedResults.NotFound();
        }

        if (await access.FindRolesAsync(brandId, userId, http.RequestAborted) is not { } roles)
        {
            return TypedResults.NotFound();
        }

        var requirement = http.GetEndpoint()?.Metadata.GetMetadata<BrandRoleRequirement>();
        if (requirement is not null && (roles & requirement.Roles) == BrandRoles.None)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You don't have the role this needs.");
        }

        member.Set(brandId, userId, roles);
        await database.UseBrandAsync(brandId, http.RequestAborted);
        return await next(context);
    }
}
