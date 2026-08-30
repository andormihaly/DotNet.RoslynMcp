using DotNet.RoslynMcp.Symbols;
using DotNet.RoslynMcp.Workspace;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

MSBuildLocator.RegisterDefaults();

var solutionPath = args
    .SkipWhile(arg => !string.Equals(arg, "--solution", StringComparison.OrdinalIgnoreCase))
    .Skip(1)
    .FirstOrDefault();

if (string.IsNullOrWhiteSpace(solutionPath))
{
    throw new ArgumentException("Missing required --solution argument.");
}

if (!File.Exists(solutionPath))
{
    throw new FileNotFoundException($"Solution file not found: {solutionPath}", solutionPath);
}



var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new WorkspaceOptions
{
    SolutionPath = solutionPath
});

builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();


builder.Services.AddSingleton<SymbolResolver>();
builder.Services.AddSingleton<WorkspaceManager>();
builder.Services.AddHostedService<WorkspaceStarter>();


var app = builder.Build();

await app.RunAsync();