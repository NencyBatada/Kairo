using System.Security.Claims;
using System.Text.Encodings.Web;
using Kairo.Contracts.Operations;
using Kairo.Modules.Operations.Data;
using Kairo.SharedKernel.Agents;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kairo.Modules.Operations;

public sealed class OperationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = UsagePolicy.Limits(configuration);
        services.AddDbContext<OperationsDbContext>((provider, options) => options.UseKairoDatabase(provider, OperationsDbContext.SchemaName));
        services.AddScoped<UsageLedger>();
        services.AddScoped<IUsageLedger>(provider => provider.GetRequiredService<UsageLedger>());
        services.AddScoped<IAgentInvocations, UsageInvocations>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, UsageAuthentication>(UsageAuthentication.Scheme, _ => { });
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        var callbacks = api.MapGroup("/brands/{brandId:guid}/operations/invocations/{invocationId:guid}")
            .RequireAuthorization(new AuthorizationPolicyBuilder(UsageAuthentication.Scheme).RequireAuthenticatedUser().Build())
            .AddEndpointFilter<UsageErrorFilter>();
        callbacks.MapPost("/reserve", async (Guid invocationId, ReserveCall request, UsageLedger ledger, CancellationToken ct) =>
        {
            var call = await ledger.ReserveAsync(invocationId, request, ct);
            return TypedResults.Ok(new { call.RequestId });
        }).WithoutRequestTransaction();
        callbacks.MapPost("/calls/{requestId:guid}/report", async (Guid invocationId, Guid requestId, ReportCall request, UsageLedger ledger, CancellationToken ct) =>
        {
            await ledger.ReportAsync(invocationId, requestId, request, ct);
            return TypedResults.Ok(new { recorded = true });
        }).WithoutRequestTransaction();
    }

    public void MapJobs(IEndpointRouteBuilder jobs) { }
    public Task MigrateAsync(IServiceProvider services, CancellationToken ct) => services.GetRequiredService<OperationsDbContext>().Database.MigrateAsync(ct);
}

internal sealed class UsageErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return await next(context); }
        catch (UsageFailure failure) { return TypedResults.Problem(statusCode: failure.Status, title: failure.Code, extensions: new Dictionary<string, object?> { ["code"] = failure.Code }); }
    }
}

internal sealed class UsageAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    OperationsDbContext db, KairoDatabase database, TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "Kairo.UsageInvocation";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.RouteValues["brandId"]?.ToString(), out var brand)
            || !Guid.TryParse(Request.RouteValues["invocationId"]?.ToString(), out var id)
            || Request.Headers["X-Kairo-Usage-Token"].ToString() is not { Length: 64 } token)
            return AuthenticateResult.NoResult();
        await database.UseBrandAsync(brand, Context.RequestAborted);
        var hash = UsagePolicy.Hash(token);
        if (!await db.Invocations.AsNoTracking().AnyAsync(row => row.Id == id && row.TokenHash == hash && row.ExpiresAt > time.GetUtcNow(), Context.RequestAborted))
            return AuthenticateResult.Fail("The invocation is unavailable.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("kairo_usage_invocation", id.ToString())], Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
}

internal sealed class UsageInvocations(IServiceScopeFactory scopes, KairoDatabase database, AgentUsageContext context) : IAgentInvocations
{
    public async Task<AgentInvocation> OpenAsync(string operation, CancellationToken ct)
    {
        var brand = database.BrandId ?? throw new AgentCallException(AgentCallException.Failed, "Usage accounting requires a brand scope.");
        var usage = context.Current ?? new AgentUsageScope(brand);
        if (usage.BrandId != brand) throw new AgentCallException(AgentCallException.Failed, "Usage accounting brand scope differs.");
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, ct);
        return await scope.ServiceProvider.GetRequiredService<UsageLedger>().OpenAsync(usage, operation, ct);
    }

    public async Task CloseAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(database.BrandId!.Value, ct);
        await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().Invocations.Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Closed, true), ct);
    }
}
