using DotNet.RoslynMcp.Symbols;
using DotNet.RoslynMcp.Workspace;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class SymbolTools(WorkspaceManager workspaceManager, SymbolResolver symbolResolver)
{
    [McpServerTool, Description("Finds symbols in the loaded .NET solution by name.")]
    public async Task<string> FindSymbol(string symbolName, CancellationToken cancellationToken)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var symbols = await symbolResolver.FindByNameAsync(workspaceManager, symbolName, cancellationToken);
        return string.Join(Environment.NewLine, symbols.Select(symbol => symbol.ToDisplayString()));
    }
}