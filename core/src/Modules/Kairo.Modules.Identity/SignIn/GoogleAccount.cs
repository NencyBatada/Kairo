using System.Security.Claims;

namespace Kairo.Modules.Identity.SignIn;

internal sealed record GoogleAccount(string Subject, string Email, bool EmailVerified, string Name, string? PictureUrl)
{
    public static GoogleAccount From(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue("email") ?? "";
        return new GoogleAccount(
            principal.FindFirstValue("sub") ?? throw new InvalidOperationException("Google returned no account ID."),
            email,
            bool.TryParse(principal.FindFirstValue("email_verified"), out var verified) && verified,
            principal.FindFirstValue("name") ?? email,
            principal.FindFirstValue("picture"));
    }
}
