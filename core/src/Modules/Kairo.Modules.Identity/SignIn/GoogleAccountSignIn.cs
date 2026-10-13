using System.Security.Claims;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Users;

namespace Kairo.Modules.Identity.SignIn;

internal sealed class GoogleAccountSignIn(IdentityDbContext db, TimeProvider time)
{
    public const string UnverifiedEmail = "unverified_email";

    public async Task<ClaimsPrincipal?> SignInAsync(GoogleAccount account, CancellationToken cancellationToken)
    {
        if (!account.EmailVerified)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleSubject == account.Subject, cancellationToken);
        if (user is null)
        {
            user = new User { GoogleSubject = account.Subject, CreatedAt = now };
            db.Users.Add(user);
        }

        user.Email = account.Email.Trim().ToLowerInvariant();
        user.Name = account.Name;
        user.PictureUrl = account.PictureUrl;
        user.LastSignedInAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var identity = new ClaimsIdentity(
            [
                new Claim(KairoClaims.UserId, user.Id.ToString()),
                new Claim(KairoClaims.Name, user.Name),
                new Claim(KairoClaims.Email, user.Email),
            ],
            IdentitySchemes.Session,
            KairoClaims.Name,
            null);
        return new ClaimsPrincipal(identity);
    }
}
