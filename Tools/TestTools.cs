using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public static class TestTools
{
    [McpServerTool, Description("Tests whether the DotNet Roslyn MCP server is running.")]
    public static string Ping() => "pong";

    [McpServerTool, Description("Returns Roslyn project and compilation information for the loaded solution.")]
    public static async Task<string> InspectSolution(WorkspaceManager workspaceManager, CancellationToken cancellationToken)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        if (solution is null)
        {
            throw new InvalidOperationException("Workspace is not initialized.");
        }

        var results = new List<string>();

        
        foreach (var project in solution.Projects)
        {
            var compilation = workspaceManager.GetCompilation(project.Id);
            results.Add($"{project.Name}: Documents={project.DocumentIds.Count}, SyntaxTrees={compilation?.SyntaxTrees.Count() ?? 0}");
        }

        return string.Join(Environment.NewLine, results);
    }
}