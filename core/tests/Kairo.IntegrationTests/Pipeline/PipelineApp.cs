using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kairo.IntegrationTests.Persistence;
using Kairo.SharedKernel;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Http;
using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kairo.IntegrationTests.Pipeline;

internal sealed class PipelineApp(WebApplication app) : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";

    public HttpClient Client { get; } = app.GetTestClient();

    public static async Task<PipelineApp> StartAsync(string connectionString, FakeBrandAccess access)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["ConnectionStrings:kairo"] = connectionString;
        builder.Services.AddKairoCore();
        builder.Services.AddProblemDetails();
        builder.Services.AddDbContext<NotesDbContext>((provider, options) => options.UseKairoDatabase(provider, "notes"));
        builder.Services.AddSingleton<IBrandAccess>(access);
        builder.Services.AddAuthentication(TestUser.SchemeName).AddScheme<AuthenticationSchemeOptions, TestUser>(TestUser.SchemeName, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        var api = app.MapKairoApi();
        api.MapGet("/ping", () => TypedResults.Ok());
        api.MapPost("/ping", () => TypedResults.Ok());

        var brand = api.MapBrandApi();
        brand.MapGet("/notes", async (NotesDbContext db) =>
            TypedResults.Ok(await db.Notes.OrderBy(note => note.Text).Select(note => note.Text).ToListAsync()));
        brand.MapPost("/notes", async (NoteRequest request, NotesDbContext db) =>
        {
            db.Notes.Add(new Note { Text = request.Text });
            await db.SaveChangesAsync();
            return TypedResults.Ok();
        });
        brand.MapPost("/notes/then-refuse", async (NoteRequest request, NotesDbContext db) =>
        {
            db.Notes.Add(new Note { Text = request.Text });
            await db.SaveChangesAsync();
            return TypedResults.Conflict();
        });
        brand.MapPost("/notes/then-throw", async (NoteRequest request, NotesDbContext db) =>
        {
            db.Notes.Add(new Note { Text = request.Text });
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Something broke after saving.");
        });
        brand.MapGet("/admin-only", () => TypedResults.Ok()).RequireBrandRole(BrandRoles.Admin);
        brand.MapGet("/whoami", (CurrentMember member) =>
            TypedResults.Ok(new MemberEcho(member.BrandId, member.UserId, member.Roles.ToString())));

        await app.StartAsync();
        return new PipelineApp(app);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid? user = null, object? body = null, bool withHeader = true)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (user is { } id)
        {
            request.Headers.Add(UserHeader, id.ToString());
        }

        if (withHeader)
        {
            request.Headers.Add(KairoApi.RequestHeader, "1");
        }

        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }
}

internal sealed record NoteRequest(string Text);

internal sealed record MemberEcho(Guid BrandId, Guid UserId, string Roles);

internal sealed class FakeBrandAccess : IBrandAccess
{
    private readonly ConcurrentDictionary<(Guid BrandId, Guid UserId), BrandRoles> _members = new();

    public void Add(Guid brandId, Guid userId, BrandRoles roles) => _members[(brandId, userId)] = roles;

    public Task<BrandRoles?> FindRolesAsync(Guid brandId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_members.TryGetValue((brandId, userId), out var roles) ? roles : (BrandRoles?)null);
}

internal sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PipelineApp.UserHeader, out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(KairoClaims.UserId, userId.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
