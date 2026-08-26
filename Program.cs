using DotNet.RoslynMcp.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Build.Locator;

MSBuildLocator.RegisterDefaults();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();

builder.Services.AddSingleton<SolutionLoader>();

var app = builder.Build();

await app.RunAsync();