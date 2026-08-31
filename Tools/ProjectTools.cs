using DotNet.RoslynMcp.Workspace;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class ProjectTools(WorkspaceManager workspaceManager)
{
    [McpServerTool, Description("Returns information about a project in the loaded .NET solution.")]
    public async Task<string> GetProjectInfo(string projectName, CancellationToken cancellationToken = default)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        var project = solution.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase));

        if (project is null)
        {
            return $"Project not found: {projectName}";
        }

        var lines = new List<string>
        {
            $"Project: {project.Name}",
            $"Language: {project.Language}",
            $"File: {project.FilePath}",
            $"Assembly: {project.AssemblyName}",
            $"Documents: {project.DocumentIds.Count}"
        };

        var projectReferences = project.ProjectReferences
            .Select(reference => solution.GetProject(reference.ProjectId))
            .Where(referencedProject => referencedProject is not null)
            .ToList();

        if (projectReferences.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Project references:");

            foreach (var referencedProject in projectReferences)
            {
                lines.Add($"  {referencedProject!.Name}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
    [McpServerTool, Description("Returns the project dependency graph of the loaded .NET solution.")]
    public async Task<string> GetDependencyGraph(CancellationToken cancellationToken = default)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        var lines = new List<string>();

        foreach (var project in solution.Projects.OrderBy(project => project.Name))
        {
            lines.Add(project.Name);

            var dependencies = project.ProjectReferences
                .Select(reference => solution.GetProject(reference.ProjectId))
                .Where(dependency => dependency is not null)
                .OrderBy(dependency => dependency!.Name)
                .ToList();

            if (dependencies.Count == 0)
            {
                lines.Add("  -> none");
            }
            else
            {
                foreach (var dependency in dependencies)
                {
                    lines.Add($"  -> {dependency!.Name}");
                }
            }

            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}