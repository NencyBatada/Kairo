using System.Text.Json;
using System.Text.Json.Serialization;
using Kairo.Api;
using Kairo.Modules.Approvals;
using Kairo.Modules.Ask;
using Kairo.Modules.Audit;
using Kairo.Modules.BrandKit;
using Kairo.Modules.BrandMemory;
using Kairo.Modules.Brands;
using Kairo.Modules.CampaignRooms;
using Kairo.Modules.Care;
using Kairo.Modules.Catalogue;
using Kairo.Modules.Identity;
using Kairo.Modules.Launch;
using Kairo.Modules.Live;
using Kairo.Modules.Measurement;
using Kairo.Modules.Notifications;
using Kairo.Modules.Opportunities;
using Kairo.Modules.Triggers;
using Kairo.SharedKernel;
using Kairo.SharedKernel.Http;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerClient("kairo");
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
{
    builder.AddRedisClient("redis");
}
builder.Services.AddKairoCore();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler(options =>
    options.StatusCodeSelector = exception =>
        exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddKairoOpenApi();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

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
    new LiveModule(),
    new AuditModule(),
    new AskModule(),
];

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
    builder.Services.AddSingleton(module);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

var api = app.MapKairoApi();
foreach (var module in modules)
{
    module.MapApi(api);
}

app.Run();
