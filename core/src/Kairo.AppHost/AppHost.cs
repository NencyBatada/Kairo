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
    .WaitForCompletion(migrator)
    .WithEnvironment("Authentication__Google__ClientId", googleClientId)
    .WithEnvironment("Authentication__Google__ClientSecret", googleClientSecret)
    .WithEnvironment("Kairo__PublicOrigin", "http://localhost:8000")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health");

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
