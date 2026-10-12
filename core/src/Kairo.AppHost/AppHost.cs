#pragma warning disable ASPIREJAVASCRIPT001

var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("kairo");

var redis = builder.AddRedis("redis");

var agents = builder.AddUvicornApp("agents", "../../../agents", "kairo_agents.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Kairo_Api>("api")
    .WithReference(sql).WaitFor(sql)
    .WithReference(redis).WaitFor(redis)
    .WithReference(agents)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WithHttpHealthCheck("/health");

builder.AddNextJsApp("web", "../../../web")
    .WithReference(api)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
