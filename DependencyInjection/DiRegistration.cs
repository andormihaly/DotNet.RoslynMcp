namespace DotNet.RoslynMcp.DependencyInjection;

public sealed record DiRegistration(string Lifetime, string Service, string Implementation, string File, int Line);