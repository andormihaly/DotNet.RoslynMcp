using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNet.RoslynMcp.Search;

public sealed class SemanticSearchService(WorkspaceManager workspaceManager)
{
    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync( string query, int maxResults = 20,  CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        }

        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        query = query.Trim();

        return await SearchCoreAsync(query, maxResults, cancellationToken);
    }

    private async Task<IReadOnlyList<SemanticSearchResult>> SearchCoreAsync(
     string query,
     int maxResults,
     CancellationToken cancellationToken)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        var exactMatches = new List<SemanticSearchResult>();
        var partialMatches = new List<SemanticSearchResult>();
        var sourceMatches = new List<SemanticSearchResult>();

        foreach (var project in solution.Projects)
        {
            var compilation = workspaceManager.GetCompilation(project.Id);

            var symbols = compilation.GetSymbolsWithName(
                name => name.Contains(query, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.TypeAndMember,
                cancellationToken);

            foreach (var symbol in symbols)
            {
                var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                if (location is null)
                {
                    continue;
                }

                var lineSpan = location.GetLineSpan();

                var matchKind = symbol.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
                    ? SemanticSearchMatchKind.ExactSymbol
                    : SemanticSearchMatchKind.PartialSymbol;

                var result = new SemanticSearchResult(
                    symbol.ToDisplayString(),
                    lineSpan.Path,
                    lineSpan.StartLinePosition.Line + 1,
                    symbol.Name,
                    matchKind);

                if (matchKind == SemanticSearchMatchKind.ExactSymbol)
                {
                    exactMatches.Add(result);
                }
                else
                {
                    partialMatches.Add(result);
                }
            }

            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken);

                if (root is null)
                {
                    continue;
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

                if (semanticModel is null)
                {
                    continue;
                }

                foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
                {
                    var ownText = string.Concat(
                                     declaration
                                    .DescendantTokens(descendIntoTrivia: true)
                                    .Where(token => token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault() == declaration)
                                    .Select(token => token.ToFullString()));

                    if (!ownText.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);

                    if (symbol is null)
                    {
                        continue;
                    }

                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        continue;
                    }

                    var lineSpan = location.GetLineSpan();

                    sourceMatches.Add(
                        new SemanticSearchResult(
                            symbol.ToDisplayString(),
                            lineSpan.Path,
                            lineSpan.StartLinePosition.Line + 1,
                            query,
                            SemanticSearchMatchKind.SourceText));
                }
            }
        }

        return exactMatches.Concat(partialMatches).Concat(sourceMatches).GroupBy(result => (result.Symbol, result.File, result.Line)).Select(group => group.First()).Take(maxResults).ToList();
    }
}