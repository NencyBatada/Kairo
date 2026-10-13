using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Connections.Accounts;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal static class AccountEndpoints
{
    public const string ConnectFirst = "Connect this product first.";
    public const string AccessLost = "Google no longer accepts Kairo's access to this account. Connect it again.";
    public const string GoogleDidNotAnswer = "Google didn't answer. Try again in a minute.";

    public static async Task<Results<Ok<ProductAccount[]>, NotFound, ProblemHttpResult>> ListAsync(
        string product,
        BrandsDbContext db,
        ConnectionTokens tokens,
        ProductCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        if (await UsableAsync(db, info, time, cancellationToken) is not { } connection)
        {
            return Conflict(ConnectFirst);
        }

        if (await tokens.GetAsync(connection, fresh: false, cancellationToken) is not { } token)
        {
            return Conflict(AccessLost);
        }

        try
        {
            return TypedResults.Ok((await catalog.For(info.Product).ListAsync(token.Value, cancellationToken)).ToArray());
        }
        catch (HttpRequestException)
        {
            return Conflict(GoogleDidNotAnswer);
        }
    }

    public static async Task<Results<Ok<ConnectionView>, NotFound, ValidationProblem, ProblemHttpResult>> ChooseAsync(
        string product,
        ChooseAccountRequest request,
        BrandsDbContext db,
        ConnectionTokens tokens,
        ProductCatalog catalog,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ExternalId))
        {
            return Invalid("Choose an account.");
        }

        if (await UsableAsync(db, info, time, cancellationToken) is not { } connection)
        {
            return Conflict(ConnectFirst);
        }

        if (await tokens.GetAsync(connection, fresh: false, cancellationToken) is not { } token)
        {
            return Conflict(AccessLost);
        }

        var accounts = catalog.For(info.Product);
        ProductAccount? account;
        string? problem;
        try
        {
            account = (await accounts.ListAsync(token.Value, cancellationToken)).FirstOrDefault(candidate => candidate.Id == request.ExternalId);
            if (account is null)
            {
                return Invalid("Choose one of the accounts this Google account can reach.");
            }

            problem = await accounts.TestAsync(token.Value, account, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Conflict(GoogleDidNotAnswer);
        }

        if (problem is not null)
        {
            return Conflict(problem);
        }

        var now = time.GetUtcNow();
        var before = ConnectionLifecycle.Snapshot(connection);
        connection.ExternalId = account.Id;
        connection.DisplayName = account.Name;
        connection.ManagerId = account.ManagerId;
        connection.State = ConnectionState.Connected;
        connection.CheckedAt = now;
        connection.ReconnectReason = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("connection.account_chosen", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
            cancellationToken);
        return TypedResults.Ok(ConnectionView.Of(info, connection, now));
    }

    private static async Task<Connection?> UsableAsync(BrandsDbContext db, ProductInfo info, TimeProvider time, CancellationToken cancellationToken)
    {
        var connection = await ConnectionLifecycle.FindAsync(db, info.Product, cancellationToken);
        return ConnectionLifecycle.StateOf(connection, time.GetUtcNow()) is ConnectionState.ChoosingAccount or ConnectionState.Connected
            ? connection
            : null;
    }

    private static ProblemHttpResult Conflict(string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static ValidationProblem Invalid(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["externalId"] = [message] });
}

internal sealed record ChooseAccountRequest(string? ExternalId);
