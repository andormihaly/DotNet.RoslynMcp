using DotNet.RoslynMcp.CallGraph;
using DotNet.RoslynMcp.Search;
using DotNet.RoslynMcp.Symbols;
using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class SymbolTools(WorkspaceManager workspaceManager, SymbolResolver symbolResolver, CallGraphBuilder callGraphBuilder, SemanticSearchService semanticSearchService)
{
    [McpServerTool, Description("Finds symbols in the loaded .NET solution by name.")]
    public async Task<string> FindSymbol(string symbolName, CancellationToken cancellationToken)
    {
        var symbols = await symbolResolver.FindByNameAsync(workspaceManager, symbolName, cancellationToken);
        return string.Join(Environment.NewLine, symbols.Select(symbol => symbol.ToDisplayString()));
    }

    [McpServerTool, Description("Finds all references to a symbol in the loaded .NET solution.")]
    public async Task<string> FindReferences(string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager, symbolName, file, line, cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
                }));
        }

        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var references = await SymbolFinder.FindReferencesAsync(result.Symbol!, solution, cancellationToken);

        var locations = references
            .SelectMany(reference => reference.Locations)
            .Select(referenceLocation =>
            {
                var lineSpan = referenceLocation.Location.GetLineSpan();
                var filePath = lineSpan.Path;
                var lineNumber = lineSpan.StartLinePosition.Line + 1;
                var columnNumber = lineSpan.StartLinePosition.Character + 1;

                return $"{filePath}:{lineNumber}:{columnNumber}";
            })
            .ToList();

        var header = $"Symbol: {result.Symbol!.ToDisplayString()}{Environment.NewLine}References: {locations.Count}";

        if (locations.Count == 0)
        {
            return header;
        }

        return $"{header}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, locations)}";
    }

    [McpServerTool, Description("Finds implementations of a type in the loaded .NET solution.")]
    public async Task<string> FindImplementations(string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager, symbolName, file, line, cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
                }));
        }

        if (result.Symbol is not INamedTypeSymbol typeSymbol)
        {
            return $"Symbol is not a type: {result.Symbol!.ToDisplayString()}";
        }

        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, transitive: true, cancellationToken: cancellationToken);

        var locations = implementations
            .Select(symbol =>
            {
                var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                if (location is null)
                {
                    return symbol.ToDisplayString();
                }

                var lineSpan = location.GetLineSpan();
                var filePath = lineSpan.Path;
                var lineNumber = lineSpan.StartLinePosition.Line + 1;

                return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
            })
            .ToList();

        var header = $"Symbol: {typeSymbol.ToDisplayString()}{Environment.NewLine}Implementations: {locations.Count}";

        if (locations.Count == 0)
        {
            return header;
        }

        return $"{header}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, locations)}";
    }

    [McpServerTool, Description("Finds callers of a method in the loaded .NET solution.")]
    public async Task<string> FindCallers(string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager, symbolName, file, line, cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
                }));
        }

        if (result.Symbol is not IMethodSymbol methodSymbol)
        {
            return $"Symbol is not a method: {result.Symbol!.ToDisplayString()}";
        }

        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var callers = await SymbolFinder.FindCallersAsync(methodSymbol, solution, cancellationToken: cancellationToken);

        var locations = callers
            .Select(caller =>
            {
                var location = caller.CallingSymbol.Locations.FirstOrDefault(location => location.IsInSource);

                if (location is null)
                {
                    return caller.CallingSymbol.ToDisplayString();
                }

                var lineSpan = location.GetLineSpan();
                var filePath = lineSpan.Path;
                var lineNumber = lineSpan.StartLinePosition.Line + 1;

                return $"{caller.CallingSymbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
            })
            .ToList();

        var header = $"Symbol: {methodSymbol.ToDisplayString()}{Environment.NewLine}Callers: {locations.Count}";

        if (locations.Count == 0)
        {
            return header;
        }

        return $"{header}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, locations)}";
    }

    [McpServerTool, Description("Finds overrides of a method in the loaded .NET solution.")]
    public async Task<string> FindOverrides(string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager, symbolName, file, line, cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
                }));
        }

        if (result.Symbol is not IMethodSymbol methodSymbol)
        {
            return $"Symbol is not a method: {result.Symbol!.ToDisplayString()}";
        }

        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var overrides = await SymbolFinder.FindOverridesAsync(methodSymbol, solution, cancellationToken: cancellationToken);

        var locations = overrides
            .Select(symbol =>
            {
                var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                if (location is null)
                {
                    return symbol.ToDisplayString();
                }

                var lineSpan = location.GetLineSpan();
                var filePath = lineSpan.Path;
                var lineNumber = lineSpan.StartLinePosition.Line + 1;

                return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
            })
            .ToList();

        var header = $"Symbol: {methodSymbol.ToDisplayString()}{Environment.NewLine}Overrides: {locations.Count}";

        if (locations.Count == 0)
        {
            return header;
        }

        return $"{header}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, locations)}";
    }

    [McpServerTool, Description("Returns the source declaration of a symbol in the loaded .NET solution.")]
    public async Task<string> GetSymbolSource(string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager, symbolName, file, line, cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
                }));
        }

        var declarations = result.Symbol!.DeclaringSyntaxReferences;

        if (declarations.Length == 0)
        {
            return $"Source declaration not found: {result.Symbol.ToDisplayString()}";
        }

        var sources = new List<string>();

        foreach (var declaration in declarations)
        {
            var syntax = await declaration.GetSyntaxAsync(cancellationToken);
            var lineSpan = syntax.GetLocation().GetLineSpan();

            var filePath = lineSpan.Path;
            var startLine = lineSpan.StartLinePosition.Line + 1;
            var endLine = lineSpan.EndLinePosition.Line + 1;

            sources.Add(
                $"Symbol: {result.Symbol.ToDisplayString()}{Environment.NewLine}" +
                $"file: {filePath}{Environment.NewLine}" +
                $"lines: {startLine}-{endLine}{Environment.NewLine}{Environment.NewLine}" +
                syntax.ToFullString().Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sources);
    }

    [McpServerTool, Description("Returns the type hierarchy of a type in the loaded .NET solution.")]
    public async Task<string> GetTypeHierarchy(string symbolName, string? file = null, int? line = null, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager, symbolName, file, line, cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}  file: {filePath}{Environment.NewLine}  line: {lineNumber}";
                }));
        }

        if (result.Symbol is not INamedTypeSymbol typeSymbol)
        {
            return $"Symbol is not a type: {result.Symbol!.ToDisplayString()}";
        }

        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        var derivedClasses = typeSymbol.TypeKind == TypeKind.Class
            ? await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution, transitive: true, cancellationToken: cancellationToken)
            : [];

        var derivedInterfaces = typeSymbol.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindDerivedInterfacesAsync(typeSymbol, solution, transitive: true, cancellationToken: cancellationToken)
            : [];

        var implementations = typeSymbol.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, transitive: true, cancellationToken: cancellationToken)
            : [];

        var lines = new List<string>
    {
        $"Type: {typeSymbol.ToDisplayString()}"
    };

        if (typeSymbol.BaseType is not null)
        {
            lines.Add($"Base type: {typeSymbol.BaseType.ToDisplayString()}");
        }

        if (typeSymbol.AllInterfaces.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Interfaces:");

            foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
            {
                lines.Add($"  {interfaceSymbol.ToDisplayString()}");
            }
        }

        if (derivedClasses.Any())
        {
            lines.Add(string.Empty);
            lines.Add("Derived types:");

            foreach (var derivedClass in derivedClasses)
            {
                lines.Add($"  {derivedClass.ToDisplayString()}");
            }
        }

        if (derivedInterfaces.Any())
        {
            lines.Add(string.Empty);
            lines.Add("Derived interfaces:");

            foreach (var derivedInterface in derivedInterfaces)
            {
                lines.Add($"  {derivedInterface.ToDisplayString()}");
            }
        }

        if (implementations.Any())
        {
            lines.Add(string.Empty);
            lines.Add("Implementations:");

            foreach (var implementation in implementations)
            {
                lines.Add($"  {implementation.ToDisplayString()}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    [McpServerTool, Description("Returns the outgoing call graph of a method in the loaded .NET solution.")]
    public async Task<string> GetCallGraph(string symbolName, string? file = null, int? line = null, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        var result = await symbolResolver.ResolveAsync(workspaceManager,symbolName,file,line,cancellationToken);

        if (result.Status == SymbolResolutionStatus.NotFound)
        {
            return $"Symbol not found: {symbolName}";
        }

        if (result.Status == SymbolResolutionStatus.Ambiguous)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Candidates.Select(symbol =>
                {
                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);

                    if (location is null)
                    {
                        return symbol.ToDisplayString();
                    }

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    return $"{symbol.ToDisplayString()}{Environment.NewLine}" +
                           $"  file: {filePath}{Environment.NewLine}" +
                           $"  line: {lineNumber}";
                }));
        }

        if (result.Symbol is not IMethodSymbol methodSymbol)
        {
            return $"Symbol is not a method: {result.Symbol!.ToDisplayString()}";
        }

        var graph = await callGraphBuilder.BuildAsync(methodSymbol,maxDepth, cancellationToken);

        var lines = new List<string>();

        AddNode(graph, lines, 0);

        return string.Join(Environment.NewLine, lines);

        static void AddNode(CallGraphNode node,List<string> lines, int depth)
        {
            var indent = new string(' ', depth * 2);

            lines.Add($"{indent}{node.Method.ToDisplayString()}");

            foreach (var child in node.Calls)
            {
                AddNode(child, lines, depth + 1);
            }
        }
    }

    [McpServerTool, Description("Searches the loaded .NET solution for symbols and source declarations matching a query.")]
    public async Task<string> SemanticSearch(
    string query,
    int maxResults = 20,
    CancellationToken cancellationToken = default)
    {
        var results = await semanticSearchService.SearchAsync(query, maxResults, cancellationToken);

        if (results.Count == 0)
        {
            return $"No matches found: {query}";
        }

        return string.Join(Environment.NewLine + Environment.NewLine,results.Select(result =>
                $"[{result.MatchKind}]{Environment.NewLine}" + $"Symbol: {result.Symbol}{Environment.NewLine}" +
                $"File: {result.File}{Environment.NewLine}" + $"Line: {result.Line}{Environment.NewLine}" +
                $"Match: {result.Match}"));
    }
}