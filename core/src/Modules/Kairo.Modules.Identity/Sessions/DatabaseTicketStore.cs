using System.Security.Claims;
using System.Security.Cryptography;
using Kairo.Modules.Identity.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Kairo.Modules.Identity.Sessions;

internal sealed class DatabaseTicketStore(IServiceScopeFactory scopes, TimeProvider time, IOptions<SessionTimeouts> options)
    : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        ticket.Principal.Identities.First().AddClaim(new Claim(KairoClaims.SessionKey, key));
        var now = time.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Sessions.Add(new Session
        {
            Key = key,
            UserId = ticket.Principal.FindUserId() ?? throw new InvalidOperationException("The ticket has no user."),
            Ticket = TicketSerializer.Default.Serialize(ticket),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + options.Value.AbsoluteTimeout,
        });
        await db.SaveChangesAsync();
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Key == key);
        if (session is null)
        {
            return;
        }

        session.Ticket = TicketSerializer.Default.Serialize(ticket);
        session.LastSeenAt = time.GetUtcNow();
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key);
        if (session is null)
        {
            return null;
        }

        if (time.GetUtcNow() >= session.ExpiresAt)
        {
            await db.Sessions.Where(s => s.Key == key).ExecuteDeleteAsync();
            return null;
        }

        return TicketSerializer.Default.Deserialize(session.Ticket);
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Sessions.Where(s => s.Key == key).ExecuteDeleteAsync();
    }
}
