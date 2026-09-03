namespace DotNet.RoslynMcp.Endpoints;

public sealed record EndpointInfo(string HttpMethod, string Route, string Handler, string Kind, string? Controller, string? Action, string Authorization, string File, int Line);