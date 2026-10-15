#pragma warning disable ASPIREJAVASCRIPT001

var builder = DistributedApplication.CreateBuilder(args);

var googleClientId = builder.AddParameter("google-client-id");
var googleClientSecret = builder.AddParameter("google-client-secret", secret: true);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("kairo");

var redis = builder.AddRedis("redis");

var agents = builder.AddUvicornApp("agents", "../../../agents", "kairo_agents.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health");

var migrator = builder.AddProject<Projects.Kairo_Migrator>("migrator")
    .WithReference(sql).WaitFor(sql);

var api = builder.AddProject<Projects.Kairo_Api>("api")
    .WithReference(sql).WaitFor(sql)
    .WithReference(redis).WaitFor(redis)
    .WithReference(agents)
    .WithEnvironment("Kairo__Agents__Url", agents.GetEndpoint("http"))
    .WaitForCompletion(migrator)
    .WithEnvironment("Authentication__Google__ClientId", googleClientId)
    .WithEnvironment("Authentication__Google__ClientSecret", googleClientSecret)
    .WithEnvironment("Kairo__PublicOrigin", "http://localhost:8000")
    .WithHttpHealthCheck("/health");

var mailSender = builder.Configuration["Kairo:MailSender"];
var mailSenderRefreshToken = builder.Configuration["Kairo:MailSenderRefreshToken"];
if (!string.IsNullOrWhiteSpace(mailSender) && !string.IsNullOrWhiteSpace(mailSenderRefreshToken))
{
    api.WithEnvironment("Kairo__Notifications__Sender", mailSender)
        .WithEnvironment("Kairo__Notifications__SenderRefreshToken", mailSenderRefreshToken);
}

var worker = builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WithEnvironment("Kairo__Agents__Url", agents.GetEndpoint("http"))
    .WaitForCompletion(migrator)
    .WithEnvironment("Authentication__Google__ClientId", googleClientId)
    .WithEnvironment("Authentication__Google__ClientSecret", googleClientSecret)
    .WithHttpHealthCheck("/health");

if (builder.Configuration["Parameters:analytics-project"] is { Length: > 0 } analyticsProject)
{
    api.WithEnvironment("Kairo__Analytics__Project", analyticsProject);
    worker.WithEnvironment("Kairo__Analytics__Project", analyticsProject);
}

if (builder.Configuration["Parameters:ai-project"] is { Length: > 0 } aiProject)
{
    agents.WithEnvironment("GOOGLE_CLOUD_PROJECT", aiProject)
        .WithEnvironment("GOOGLE_CLOUD_LOCATION", "global");
}

var web = builder.AddNextJsApp("web", "../../../web")
    .WithEnvironment("KAIRO_API_URL", api.GetEndpoint("http"))
    .WithHttpHealthCheck("/health");

builder.AddYarp("gateway")
    .WithHostPort(8000)
    .WithConfiguration(yarp =>
    {
        yarp.AddRoute("/api/{**catch-all}", api);
        yarp.AddRoute("/{**catch-all}", web);
    });

builder.Build().Run();
