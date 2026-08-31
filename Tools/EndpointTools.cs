using DotNet.RoslynMcp.Endpoints;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class EndpointTools(EndpointMapService endpointMapService)
{
    [McpServerTool, Description("Returns the ASP.NET Core controller endpoint map of the loaded .NET solution.")]
    public async Task<string> GetEndpointMap(CancellationToken cancellationToken = default)
    {
        var endpoints = await endpointMapService.GetEndpointsAsync(cancellationToken);

        if (endpoints.Count == 0) return "No endpoints found.";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            endpoints.Select(endpoint =>
                $"{endpoint.HttpMethod} {endpoint.Route}{Environment.NewLine}" +
                $"Symbol: {endpoint.Symbol}{Environment.NewLine}" +
                $"File: {endpoint.File}{Environment.NewLine}" +
                $"Line: {endpoint.Line}"));
    }
}