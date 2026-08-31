namespace DotNet.RoslynMcp.Search;

public sealed record SemanticSearchResult(string Symbol, string File, int Line, string Match, SemanticSearchMatchKind MatchKind);