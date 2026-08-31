using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;

namespace DotNet.RoslynMcp.Symbols;

public sealed class SymbolResolver
{
    public async Task<IReadOnlyList<ISymbol>> FindByNameAsync(WorkspaceManager workspace, string symbolName, CancellationToken cancellationToken = default)
    {
        var results = new List<ISymbol>();
        var solution = await workspace.GetSolutionAsync(cancellationToken);

        var lastDotIndex = symbolName.LastIndexOf('.');
        var simpleName = lastDotIndex >= 0 ? symbolName[(lastDotIndex + 1)..] : symbolName;

        foreach (var project in solution.Projects)
        {
            var compilation = workspace.GetCompilation(project.Id);
            var symbols = compilation.GetSymbolsWithName(simpleName, SymbolFilter.TypeAndMember, cancellationToken);

            foreach (var symbol in symbols)
            {
                if (lastDotIndex < 0)
                {
                    results.Add(symbol);
                    continue;
                }

                var displayName = symbol.ToDisplayString();
                var parameterListIndex = displayName.IndexOf('(');
                var qualifiedName = parameterListIndex >= 0 ? displayName[..parameterListIndex] : displayName;

                if (qualifiedName.Equals(symbolName, StringComparison.Ordinal) ||
                    qualifiedName.EndsWith($".{symbolName}", StringComparison.Ordinal))
                {
                    results.Add(symbol);
                }
            }
        }

        return results;
    }

    public async Task<SymbolResolutionResult> ResolveAsync(WorkspaceManager workspace, string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        if (line.HasValue && string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException("File must be provided when line is specified.", nameof(file));
        }

        var candidates = (await FindByNameAsync(workspace, symbolName, cancellationToken)).ToList();

        if (!string.IsNullOrWhiteSpace(file))
        {
            candidates = candidates
                .Where(symbol => symbol.Locations.Any(location =>
                    location.IsInSource &&
                    string.Equals(location.SourceTree?.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (line.HasValue)
        {
            candidates = candidates
                .Where(symbol => symbol.Locations.Any(location =>
                {
                    if (!location.IsInSource)
                    {
                        return false;
                    }

                    var lineSpan = location.GetLineSpan();
                    var startLine = lineSpan.StartLinePosition.Line + 1;
                    var endLine = lineSpan.EndLinePosition.Line + 1;

                    return line.Value >= startLine && line.Value <= endLine;
                }))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return new SymbolResolutionResult(SymbolResolutionStatus.NotFound, null, candidates);
        }

        if (candidates.Count == 1)
        {
            return new SymbolResolutionResult(SymbolResolutionStatus.Success, candidates[0], candidates);
        }

        return new SymbolResolutionResult(SymbolResolutionStatus.Ambiguous, null, candidates);
    }
}