using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Modules.Ask.Data;
using Kairo.SharedKernel.Files;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Kairo.Modules.Ask;

public sealed class AskModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AskDbContext>((provider, options) => options.UseKairoDatabase(provider, AskDbContext.SchemaName));
        services.AddScoped<AskService>();
        services.AddScoped<AskJobs>();
        services.AddScoped<AskPreviewRunner>();
        services.AddScoped<IAskResource, WorkspaceResource>();
        services.AddSingleton<AskLoopSwitch>();
        services.AddHostedService<AskLoop>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AskAuthentication>(AskAuthentication.SchemeName, _ => { });
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.ServiceProvider.GetRequiredService<AskLoopSwitch>().Enable();
        var ask = api.MapBrandApi().MapGroup("/ask").AddEndpointFilter<AskErrorFilter>();
        var conversations = ask.MapGroup("/conversations");
        conversations.MapGet("", async (AskService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)));
        conversations.MapPost("", async (AskService service, CancellationToken ct) => TypedResults.Ok(await service.CreateAsync(ct)));
        conversations.MapGet("/{conversationId:guid}", async (Guid conversationId, AskService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(conversationId, ct)));
        conversations.MapPost("/{conversationId:guid}/messages", async (Guid conversationId, SendMessage request, AskService service, CancellationToken ct) => TypedResults.Ok(await service.SendAsync(conversationId, request, ct))).WithoutRequestTransaction();
        conversations.MapGet("/{conversationId:guid}/proposals/{proposalId:guid}", async (Guid conversationId, Guid proposalId, AskService service, CancellationToken ct) => TypedResults.Ok(await service.ProposalAsync(conversationId, proposalId, ct)));
        conversations.MapGet("/{conversationId:guid}/proposals/{proposalId:guid}/files/{name}", PreviewFileAsync);
        conversations.MapPost("/{conversationId:guid}/proposals/{proposalId:guid}/confirm", async (Guid conversationId, Guid proposalId, AskService service, CancellationToken ct) => TypedResults.Ok(await service.DecideAsync(conversationId, proposalId, true, ct)));
        conversations.MapPost("/{conversationId:guid}/proposals/{proposalId:guid}/reject", async (Guid conversationId, Guid proposalId, AskService service, CancellationToken ct) => TypedResults.Ok(await service.DecideAsync(conversationId, proposalId, false, ct)));
        ask.MapPost("/invocations/{messageId:guid}/tools", async (Guid messageId, ToolRequest request, AskService service, CancellationToken ct) => TypedResults.Ok(await service.ToolAsync(messageId, request, ct)))
            .RequireAuthorization(new AuthorizationPolicyBuilder(AskAuthentication.SchemeName).RequireAuthenticatedUser().Build());
    }

    public void MapJobs(IEndpointRouteBuilder jobs) => jobs.MapPost("/ask/previews", async (AskJobs jobs, CancellationToken ct) => TypedResults.Ok(new { ran = await jobs.RunDueAsync(ct) }));
    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => services.GetRequiredService<AskDbContext>().Database.MigrateAsync(cancellationToken);

    private static async Task<Results<FileStreamHttpResult, NotFound>> PreviewFileAsync(Guid conversationId, Guid proposalId, string name,
        AskService service, IFileStore files, HttpContext http, CancellationToken ct)
    {
        var file = await service.PreviewFileAsync(conversationId, proposalId, name, ct);
        if (file.ContentType is not ("image/jpeg" or "image/png" or "video/mp4" or "audio/mpeg")) return TypedResults.NotFound();
        try
        {
            var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, file.Key, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "sandbox";
            return TypedResults.Stream(stream, file.ContentType, enableRangeProcessing: true);
        }
        catch (StoredFileMissingException) { return TypedResults.NotFound(); }
    }
}

internal sealed class AskErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "private, no-store";
        try { return await next(context); }
        catch (AskResourceException exception) { return TypedResults.Problem(statusCode: exception.Status, title: exception.Message); }
        catch (JsonException) { return TypedResults.Problem(statusCode: 400, title: "The change doesn't match the supported fields."); }
    }
}
