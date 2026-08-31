using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;

namespace DotNet.RoslynMcp.Symbols;

public sealed class SymbolResolver
{
    public async Task<IReadOnlyList<ISymbol>> FindByNameAsync(WorkspaceManager workspace, string symbolName, CancellationToken cancellationToken = default)
    {
        var results = new List<ISymbol>();

        var solution = await workspace.GetSolutionAsync(cancellationToken);

        foreach (var project in solution.Projects)
        {
            var compilation = workspace.GetCompilation(project.Id);
            var symbols = compilation.GetSymbolsWithName(symbolName, SymbolFilter.TypeAndMember, cancellationToken);

            results.AddRange(symbols);
        }

        return results;
    }
}