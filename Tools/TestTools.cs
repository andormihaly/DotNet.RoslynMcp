using DotNet.RoslynMcp.Workspace;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public static class TestTools
{
    [McpServerTool, Description("Tests whether the DotNet Roslyn MCP server is running.")]
    public static string Ping() => "pong";

    [McpServerTool, Description("Loads a .NET solution and returns the number of projects.")]
    public static async Task<string> LoadSolution(string solutionPath, SolutionLoader solutionLoader, CancellationToken cancellationToken)
    {
        var solution = await solutionLoader.LoadAsync(solutionPath, cancellationToken);
        return $"Solution '{solution.FilePath}' loaded successfully. Projects: {solution.ProjectIds.Count}";
    }
}