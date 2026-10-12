using Kairo.Modules.Approvals;
using Kairo.Modules.Audit;
using Kairo.Modules.BrandKit;
using Kairo.Modules.BrandMemory;
using Kairo.Modules.Brands;
using Kairo.Modules.CampaignRooms;
using Kairo.Modules.Care;
using Kairo.Modules.Catalogue;
using Kairo.Modules.Identity;
using Kairo.Modules.Launch;
using Kairo.Modules.Measurement;
using Kairo.Modules.Notifications;
using Kairo.Modules.Opportunities;
using Kairo.Modules.Triggers;
using Kairo.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerClient("kairo");
builder.AddRedisClient("redis");

IModule[] modules =
[
    new IdentityModule(),
    new BrandsModule(),
    new BrandKitModule(),
    new CatalogueModule(),
    new OpportunitiesModule(),
    new TriggersModule(),
    new CampaignRoomsModule(),
    new ApprovalsModule(),
    new LaunchModule(),
    new CareModule(),
    new MeasurementModule(),
    new BrandMemoryModule(),
    new NotificationsModule(),
    new AuditModule(),
];

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
    builder.Services.AddSingleton(module);
}

var app = builder.Build();

app.MapDefaultEndpoints();

var api = app.MapGroup("/api");
foreach (var module in modules)
{
    module.MapApi(api);
}

app.Run();
