namespace DotNet.RoslynMcp.Projects;

public sealed record ProjectFileInfo( string? TargetFramework, string? Nullable, string? ImplicitUsings, IReadOnlyList<PackageReferenceInfo> PackageReferences);