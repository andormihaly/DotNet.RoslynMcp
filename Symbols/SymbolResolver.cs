using Microsoft.CodeAnalysis;

namespace DotNet.RoslynMcp.Symbols;

public sealed class SymbolResolver
{
    public async Task<IReadOnlyList<ISymbol>> FindByNameAsync(Solution solution, string symbolName, CancellationToken cancellationToken = default)
    {
        var results = new List<ISymbol>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);

            if (compilation is null)
            {
                continue;
            }

            var symbols = compilation.GetSymbolsWithName(symbolName, SymbolFilter.TypeAndMember, cancellationToken);

            results.AddRange(symbols);
        }

        return results;
    }
}