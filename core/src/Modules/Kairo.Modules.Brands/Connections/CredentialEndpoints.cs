using System.Security.Claims;

namespace Kairo.Modules.Brands.Connections;

internal static class CredentialEndpoints
{
    public const string Rejected =
        "Google rejected these credentials. Check the client ID, client secret and refresh token, then try again.";

    public static async Task<Results<Ok<ConnectionView>, NotFound, ValidationProblem, ProblemHttpResult>> ConnectAsync(
        string product,
        ClientCredentialsRequest request,
        CurrentMember member,
        ClaimsPrincipal principal,
        GoogleOAuth oauth,
        ConnectionGrants grants,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        var clientId = request.ClientId?.Trim() ?? "";
        var clientSecret = request.ClientSecret?.Trim() ?? "";
        var refreshToken = request.RefreshToken?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        Require(errors, "clientId", clientId, 255, "Enter the client ID.", "The client ID is too long.");
        Require(errors, "clientSecret", clientSecret, 512, "Enter the client secret.", "The client secret is too long.");
        Require(errors, "refreshToken", refreshToken, 2048, "Enter the refresh token.", "The refresh token is too long.");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var client = new OAuthClient(clientId, clientSecret);
        Grant? grant;
        try
        {
            grant = await oauth.RefreshAsync(refreshToken, client, cancellationToken);
        }
        catch (HttpRequestException exception) when ((int?)exception.StatusCode is >= 400 and < 500)
        {
            grant = null;
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: AccountEndpoints.GoogleDidNotAnswer);
        }

        if (grant is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: Rejected);
        }

        var missing = info.Scopes.Except(grant.Scopes).ToArray();
        if (missing.Length > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"This refresh token doesn't have the access {info.Name} needs. Grant {string.Join(", ", missing)}, then try again.");
        }

        var connection = await grants.StoreAsync(
            new NewGrant(member.BrandId, info, grant.Subject ?? "", refreshToken, client, member.UserId, principal.FindFirstValue(KairoClaims.Name) ?? ""),
            cancellationToken);
        return TypedResults.Ok(ConnectionView.Of(info, connection, time.GetUtcNow()));
    }

    private static void Require(Dictionary<string, string[]> errors, string field, string value, int maxLength, string missing, string tooLong)
    {
        if (value.Length == 0)
        {
            errors[field] = [missing];
        }
        else if (value.Length > maxLength)
        {
            errors[field] = [tooLong];
        }
    }
}

internal sealed record ClientCredentialsRequest(string? ClientId, string? ClientSecret, string? RefreshToken);
