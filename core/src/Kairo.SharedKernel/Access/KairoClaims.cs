using System.Security.Claims;

namespace Kairo.SharedKernel.Access;

public static class KairoClaims
{
    public const string UserId = "kairo_user_id";
    public const string Name = "name";
    public const string Email = "email";

    public static Guid? FindUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserId), out var id) ? id : null;
}
