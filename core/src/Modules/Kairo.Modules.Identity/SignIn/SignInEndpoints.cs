using System.Security.Claims;
using Kairo.Modules.Identity.Data;
using Microsoft.AspNetCore.Authentication;

namespace Kairo.Modules.Identity.SignIn;

internal static class SignInEndpoints
{
    public static void MapSignIn(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapGet("/sign-in", (string? returnUrl) =>
            TypedResults.Challenge(
                new AuthenticationProperties { RedirectUri = ReturnUrls.Sanitize(returnUrl) },
                [IdentitySchemes.Google]));

        auth.MapPost("/sign-out", async (HttpContext http) =>
        {
            await http.SignOutAsync(IdentitySchemes.Session);
            return TypedResults.NoContent();
        });

        api.MapGet("/me", async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> (
            ClaimsPrincipal principal,
            IdentityDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.FindUserId();
            var me = await db.Users
                .Where(user => user.Id == userId)
                .Select(user => new MeResponse(user.Id, user.Name, user.Email, user.PictureUrl))
                .SingleOrDefaultAsync(cancellationToken);
            return me is null ? TypedResults.Unauthorized() : TypedResults.Ok(me);
        }).RequireAuthorization();
    }
}

internal sealed record MeResponse(Guid Id, string Name, string Email, string? PictureUrl);
