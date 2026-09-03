using DotNet.RoslynMcp.Endpoints;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class EndpointTools(EndpointMapService endpointMapService)
{
    [McpServerTool, Description("Returns the ASP.NET Core endpoint map of the loaded .NET solution.")]
    public async Task<string> GetEndpointMap(CancellationToken cancellationToken = default)
    {
        var endpoints = await endpointMapService.GetEndpointsAsync(cancellationToken);

        if (endpoints.Count == 0) return "No endpoints found.";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            endpoints.Select(endpoint =>
            {
                var lines = new List<string>
                {
                $"{endpoint.HttpMethod} {endpoint.Route}",
                $"Kind: {endpoint.Kind}",
                $"Handler: {endpoint.Handler}"
                };

                if (endpoint.Controller is not null)
                {
                    lines.Add($"Controller: {endpoint.Controller}");
                }

                if (endpoint.Action is not null)
                {
                    lines.Add($"Action: {endpoint.Action}");
                }

                lines.Add($"Authorization: {endpoint.Authorization}");
                lines.Add($"File: {endpoint.File}");
                lines.Add($"Line: {endpoint.Line}");

                return string.Join(Environment.NewLine, lines);
            }));
    }
}