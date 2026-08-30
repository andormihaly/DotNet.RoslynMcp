using Microsoft.Extensions.Hosting;

namespace DotNet.RoslynMcp.Workspace;

public sealed class WorkspaceStarter(WorkspaceManager workspaceManager, WorkspaceOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await workspaceManager.LoadSolutionAsync(options.SolutionPath, stoppingToken);
    }
}