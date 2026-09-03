using DotNet.RoslynMcp.Projects;
using DotNet.RoslynMcp.Workspace;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class ProjectTools(
    WorkspaceManager workspaceManager,
    ProjectFileReader projectFileReader)
{
    [McpServerTool, Description("Returns detailed information about a project in the loaded .NET solution, including build settings, package references, and project dependencies.")]
    public async Task<string> GetProjectInfo(string projectName, CancellationToken cancellationToken = default)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        var project = solution.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase));

        if (project is null)
        {
            return $"Project not found: {projectName}";
        }

        var solutionDirectory = Path.GetDirectoryName(solution.FilePath)
            ?? throw new InvalidOperationException("Solution path is not available.");

        var projectFilePath = project.FilePath;
        var relativeProjectPath = projectFilePath is null
            ? "unknown"
            : Path.GetRelativePath(solutionDirectory, projectFilePath);

        var lines = new List<string>
        {
            $"Project: {project.Name}",
            $"Language: {project.Language}",
            $"File: {relativeProjectPath}",
            $"Assembly: {project.AssemblyName}",
            $"Output kind: {project.CompilationOptions?.OutputKind}",
            $"Documents: {project.DocumentIds.Count}"
        };

        if (projectFilePath is not null && File.Exists(projectFilePath))
        {
            var projectFileInfo = await projectFileReader.ReadProjectFileInfoAsync(projectFilePath, solutionDirectory, cancellationToken);

            if (!string.IsNullOrWhiteSpace(projectFileInfo.TargetFramework))
            {
                lines.Add($"Target framework: {projectFileInfo.TargetFramework}");
            }

            if (!string.IsNullOrWhiteSpace(projectFileInfo.Nullable))
            {
                lines.Add($"Nullable: {projectFileInfo.Nullable}");
            }

            if (!string.IsNullOrWhiteSpace(projectFileInfo.ImplicitUsings))
            {
                lines.Add($"Implicit usings: {projectFileInfo.ImplicitUsings}");
            }

            if (projectFileInfo.PackageReferences.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Package references:");

                foreach (var packageReference in projectFileInfo.PackageReferences.OrderBy(package => package.Name))
                {
                    lines.Add(string.IsNullOrWhiteSpace(packageReference.Version)
                        ? $"  {packageReference.Name}"
                        : $"  {packageReference.Name} ({packageReference.Version})");
                }
            }
        }

        var projectReferences = project.ProjectReferences
            .Select(reference => solution.GetProject(reference.ProjectId))
            .Where(referencedProject => referencedProject is not null)
            .OrderBy(referencedProject => referencedProject!.Name)
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
            var references = project.ProjectReferences
                .Select(reference => solution.GetProject(reference.ProjectId))
                .Where(referencedProject => referencedProject is not null)
                .Select(referencedProject => referencedProject!.Name)
                .OrderBy(name => name)
                .ToList();

            if (references.Count == 0)
            {
                lines.Add($"{project.Name} -> (none)");
                continue;
            }

            foreach (var reference in references)
            {
                lines.Add($"{project.Name} -> {reference}");
            }
        }

        return lines.Count == 0
            ? "No projects found."
            : string.Join(Environment.NewLine, lines);
    }
}