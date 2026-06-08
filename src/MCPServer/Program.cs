using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MCPServer.Services;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

builder.Services.AddSingleton<SafetyIncidentService>();

var app = builder.Build();

await app.RunAsync();