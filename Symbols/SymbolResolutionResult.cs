using Microsoft.CodeAnalysis;

namespace DotNet.RoslynMcp.Symbols;

public sealed record SymbolResolutionResult(SymbolResolutionStatus Status, ISymbol? Symbol, IReadOnlyList<ISymbol> Candidates);

