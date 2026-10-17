using System.Security.Claims;
using System.Text.Encodings.Web;
using Kairo.Modules.Ask.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kairo.Modules.Ask;

internal sealed class AskAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    AskDbContext db, KairoDatabase database, TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Kairo.AskInvocation";
    public const string InvocationClaim = "kairo_ask_invocation";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.RouteValues["brandId"]?.ToString(), out var brand)
            || !Guid.TryParse(Request.RouteValues["messageId"]?.ToString(), out var id)
            || Request.Headers["X-Kairo-Ask-Token"].ToString() is not { Length: 64 } token)
            return AuthenticateResult.NoResult();
        await database.UseBrandAsync(brand, Context.RequestAborted);
        var hash = AskPolicy.Hash(token);
        var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.TokenHash == hash
            && row.TokenExpiresAt > time.GetUtcNow() && row.Status == "running", Context.RequestAborted);
        if (message is null) return AuthenticateResult.Fail("The invocation is unavailable.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(KairoClaims.UserId, message.UserId.ToString()), new Claim(InvocationClaim, message.Id.ToString()),
        ], SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
