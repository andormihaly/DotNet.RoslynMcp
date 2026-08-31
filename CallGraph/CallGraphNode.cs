using Microsoft.CodeAnalysis;

namespace DotNet.RoslynMcp.CallGraph;

public sealed record CallGraphNode(IMethodSymbol Method, IReadOnlyList<CallGraphNode> Calls);