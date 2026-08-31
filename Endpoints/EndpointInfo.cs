namespace DotNet.RoslynMcp.Endpoints;

public sealed record EndpointInfo(string HttpMethod, string Route, string Symbol, string File, int Line);