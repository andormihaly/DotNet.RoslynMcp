using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotNet.RoslynMcp.Workspace;

public sealed class SolutionLoader
{
    public async Task<Solution> LoadAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        var workspace = MSBuildWorkspace.Create();
        return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
    }
}