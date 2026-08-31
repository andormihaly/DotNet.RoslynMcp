using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNet.RoslynMcp.CallGraph;

public sealed class CallGraphBuilder(WorkspaceManager workspaceManager)
{
    public async Task<CallGraphNode> BuildAsync(IMethodSymbol method, int maxDepth, CancellationToken cancellationToken = default)
    {
        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        return await BuildNodeAsync(method, maxDepth, visited, cancellationToken);
    }

    private async Task<CallGraphNode> BuildNodeAsync(IMethodSymbol method,int remainingDepth, HashSet<IMethodSymbol> visited, CancellationToken cancellationToken)
    {
        if (remainingDepth == 0 || !visited.Add(method))
        {
            return new CallGraphNode(method, []);
        }

        var calls = new List<CallGraphNode>();
        var calledMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = await syntaxReference.GetSyntaxAsync(cancellationToken);

            if (syntax.SyntaxTree.FilePath is null)
            {
                continue;
            }

            var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

            var document = solution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(document =>
                    string.Equals(
                        document.FilePath,
                        syntax.SyntaxTree.FilePath,
                        StringComparison.OrdinalIgnoreCase));

            if (document is null)
            {
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel is null)
            {
                continue;
            }

            var invocations = syntax
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);

                if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
                {
                    continue;
                }

                if (!calledMethods.Add(calledMethod))
                {
                    continue;
                }

                var child = await BuildNodeAsync(
                    calledMethod,
                    remainingDepth - 1,
                    visited,
                    cancellationToken);

                calls.Add(child);
            }
        }

        return new CallGraphNode(method, calls);
    }
}