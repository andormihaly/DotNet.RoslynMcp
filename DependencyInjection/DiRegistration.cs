namespace DotNet.RoslynMcp.DependencyInjection;

public sealed record DiRegistration(
    string Lifetime,
    string Service,
    string Implementation,
    string? Key,
    string File,
    int Line,
    bool IsDuplicate,
    bool HasCaptiveDependencyRisk);