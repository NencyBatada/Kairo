using System.Security.Claims;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Live;
using Kairo.Modules.Brands.Data;
using Microsoft.Data.SqlClient;

namespace Kairo.Modules.Brands.Workspaces;

internal static class WorkspaceEndpoints
{
    public static void MapWorkspaces(this IEndpointRouteBuilder api)
    {
        var workspaces = api.MapGroup("/brands").RequireAuthorization();
        workspaces.MapPost("", CreateAsync);
        workspaces.MapGet("", ListMineAsync);

        var workspace = api.MapBrandApi();
        workspace.MapGet("", GetAsync);
        workspace.MapPatch("", UpdateAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Results<Created<CreatedWorkspace>, ValidationProblem>> CreateAsync(
        CreateWorkspaceRequest request,
        ClaimsPrincipal principal,
        BrandsDbContext db,
        KairoDatabase database,
        IWorkspaceFounding founding,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = WorkspaceRules.Errors(
            ("name", WorkspaceRules.CheckName(request.Name)),
            ("handle", WorkspaceRules.CheckHandle(request.Handle)),
            ("timeZone", WorkspaceRules.CheckTimeZone(request.TimeZone)),
            ("currency", WorkspaceRules.CheckCurrency(request.Currency)));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            if (await db.Brands.AnyAsync(brand => brand.Handle == request.Handle, cancellationToken))
            {
                return HandleTaken();
            }
        }

        var userId = principal.FindUserId()!.Value;
        var brand = new Brand
        {
            Handle = request.Handle,
            Name = request.Name.Trim(),
            TimeZone = request.TimeZone,
            Currency = request.Currency,
            CreatedBy = userId,
            CreatedAt = time.GetUtcNow(),
        };
        db.Brands.Add(brand);
        await database.UseBrandAsync(brand.Id, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return HandleTaken();
        }

        await founding.FoundAsync(brand.Id, userId, brand.Currency, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand.created", "brand", brand.Id.ToString(), After: Snapshot(brand)),
            cancellationToken);
        return TypedResults.Created($"/api/brands/{brand.Id}", new CreatedWorkspace(brand.Id, brand.Handle));
    }

    private static async Task<Ok<WorkspaceSummary[]>> ListMineAsync(
        ClaimsPrincipal principal,
        IMembershipDirectory memberships,
        IBrandDirectory brands,
        CancellationToken cancellationToken)
    {
        var mine = await memberships.GetForUserAsync(principal.FindUserId()!.Value, cancellationToken);
        var roles = mine.ToDictionary(membership => membership.BrandId, membership => membership.Roles);
        var found = await brands.GetAsync(roles.Keys, cancellationToken);
        return TypedResults.Ok(found
            .Select(brand => new WorkspaceSummary(brand.Id, brand.Handle, brand.Name, roles[brand.Id].ToList()))
            .ToArray());
    }

    private static async Task<Results<Ok<WorkspaceDetails>, NotFound>> GetAsync(
        BrandsDbContext db,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(cancellationToken);
        return brand is null ? TypedResults.NotFound() : TypedResults.Ok(Details(brand, member));
    }

    private static async Task<Results<Ok<WorkspaceDetails>, ValidationProblem, NotFound>> UpdateAsync(
        UpdateWorkspaceRequest request,
        BrandsDbContext db,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        var errors = WorkspaceRules.Errors(
            ("name", request.Name is null ? null : WorkspaceRules.CheckName(request.Name)),
            ("timeZone", request.TimeZone is null ? null : WorkspaceRules.CheckTimeZone(request.TimeZone)));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var brand = await db.Brands.SingleOrDefaultAsync(cancellationToken);
        if (brand is null)
        {
            return TypedResults.NotFound();
        }

        var before = Snapshot(brand);
        brand.Name = request.Name?.Trim() ?? brand.Name;
        brand.TimeZone = request.TimeZone ?? brand.TimeZone;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand.updated", "brand", brand.Id.ToString(), before, Snapshot(brand)),
            cancellationToken);
        await live.PublishAsync(new LiveEvent("workspace.changed", new { }, LiveAudience.Everyone), cancellationToken);
        return TypedResults.Ok(Details(brand, member));
    }

    private static ValidationProblem HandleTaken() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["handle"] = ["That handle is taken."] });

    private static object Snapshot(Brand brand) =>
        new { brand.Name, brand.Handle, brand.TimeZone, brand.Currency };

    private static WorkspaceDetails Details(Brand brand, CurrentMember member) =>
        new(brand.Id, brand.Handle, brand.Name, brand.TimeZone, brand.Currency, member.Roles.ToList());
}

internal sealed record CreateWorkspaceRequest(string Name, string Handle, string TimeZone, string Currency);

internal sealed record UpdateWorkspaceRequest(string? Name, string? TimeZone);

internal sealed record CreatedWorkspace(Guid Id, string Handle);

internal sealed record WorkspaceSummary(Guid Id, string Handle, string Name, BrandRole[] Roles);

internal sealed record WorkspaceDetails(Guid Id, string Handle, string Name, string TimeZone, string Currency, BrandRole[] Roles);
