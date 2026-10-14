using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Approvals;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Invitations;
using Kairo.Modules.Identity.Members;
using Kairo.Modules.Identity.Sessions;
using Kairo.Modules.Identity.SignIn;
using Microsoft.AspNetCore.DataProtection;

namespace Kairo.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, IdentityDbContext.SchemaName));
        services.AddDataProtection()
            .SetApplicationName("Kairo")
            .PersistKeysToDbContext<IdentityDbContext>();
        services.AddKairoSignIn(configuration);
        services.AddScoped<IBrandAccess, BrandAccess>();
        services.AddScoped<IMembershipDirectory, MembershipDirectory>();
        services.AddScoped<IWorkspaceFounding, WorkspaceFounding>();
        services.AddScoped<ISessionDirectory, SessionDirectory>();
        services.AddScoped<IBrandMembers, BrandMembers>();
        services.AddScoped<IApprovers, Approvers>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapSignIn();
        api.MapMembers();
        api.MapInvitations();
        api.MapApprovalSettings();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
}
