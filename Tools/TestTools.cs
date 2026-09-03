using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public static class TestTools
{
    [McpServerTool, Description("Tests whether the DotNet Roslyn MCP server is running.")]
    public static string Ping() => "pong";

}