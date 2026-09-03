using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

namespace DotNet.RoslynMcp.DependencyInjection;



public sealed class DiRegistrationService(WorkspaceManager workspaceManager)
{
    public async Task<IReadOnlyList<DiRegistration>> GetRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        return await GetRegistrationsCoreAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DiRegistration>> GetRegistrationsCoreAsync(CancellationToken cancellationToken)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var solutionDirectory = GetSolutionDirectory(solution);

        var registrations = await CollectRegistrationsAsync(solution, solutionDirectory, cancellationToken);

        return AnalyzeRegistrations(registrations);
    }

    private static string GetSolutionDirectory(Solution solution)
    {
        return Path.GetDirectoryName(solution.FilePath)
            ?? throw new InvalidOperationException("Solution path is not available.");
    }

    private static async Task<List<ParsedDiRegistration>> CollectRegistrationsAsync(
        Solution solution,
        string solutionDirectory,
        CancellationToken cancellationToken)
    {
        var results = new List<ParsedDiRegistration>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null) continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel is null) continue;

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var registration = TryParseRegistration(invocation, semanticModel, solutionDirectory, cancellationToken);

                    if (registration is not null)
                    {
                        results.Add(registration);
                    }
                }
            }
        }

        return results;
    }

    private static ParsedDiRegistration? TryParseRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string solutionDirectory,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        var methodName = GetMethodName(memberAccess);
        var lifetime = GetLifetime(methodName);

        if (lifetime is null)
        {
            return null;
        }

        var isKeyed = IsKeyedRegistration(methodName);

        var parsedRegistration = memberAccess.Name is GenericNameSyntax genericName
            ? ParseGenericRegistration(invocation, genericName, semanticModel, isKeyed, cancellationToken)
            : ParseNonGenericRegistration(invocation, semanticModel, isKeyed, cancellationToken);

        if (parsedRegistration is null)
        {
            return null;
        }

        var key = isKeyed
            ? GetRegistrationKey(invocation, memberAccess, semanticModel, cancellationToken)
            : null;

        var lineSpan = invocation.GetLocation().GetLineSpan();
        var relativePath = Path.GetRelativePath(solutionDirectory, lineSpan.Path);

        return new ParsedDiRegistration(
            lifetime,
            parsedRegistration.Service,
            parsedRegistration.Implementation,
            key,
            relativePath,
            lineSpan.StartLinePosition.Line + 1,
            parsedRegistration.ImplementationType);
    }

    private static ParsedRegistrationData? ParseGenericRegistration(
        InvocationExpressionSyntax invocation,
        GenericNameSyntax genericName,
        SemanticModel semanticModel,
        bool isKeyed,
        CancellationToken cancellationToken)
    {
        var typeArguments = genericName.TypeArgumentList.Arguments;

        if (typeArguments.Count is < 1 or > 2)
        {
            return null;
        }

        var serviceType = semanticModel.GetTypeInfo(typeArguments[0], cancellationToken).Type;

        if (serviceType is null)
        {
            return null;
        }

        var service = serviceType.ToDisplayString();

        if (typeArguments.Count == 2)
        {
            var implementationType = semanticModel.GetTypeInfo(typeArguments[1], cancellationToken).Type;

            if (implementationType is null)
            {
                return null;
            }

            return new ParsedRegistrationData(
                service,
                implementationType.ToDisplayString(),
                implementationType as INamedTypeSymbol);
        }

        var factoryArgumentCount = isKeyed
            ? invocation.ArgumentList.Arguments.Count - 1
            : invocation.ArgumentList.Arguments.Count;

        if (factoryArgumentCount > 0)
        {
            return new ParsedRegistrationData(
                service,
                "factory",
                null);
        }

        return new ParsedRegistrationData(
            service,
            service,
            serviceType as INamedTypeSymbol);
    }

    private static ParsedRegistrationData? ParseNonGenericRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        bool isKeyed,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;

        var serviceArgumentIndex = 0;
        var implementationArgumentIndex = isKeyed ? 2 : 1;

        if (arguments.Count <= implementationArgumentIndex)
        {
            return null;
        }

        var serviceExpression = arguments[serviceArgumentIndex].Expression;

        if (serviceExpression is not TypeOfExpressionSyntax serviceTypeOf)
        {
            return null;
        }

        var serviceType = semanticModel.GetTypeInfo(serviceTypeOf.Type, cancellationToken).Type;

        if (serviceType is null)
        {
            return null;
        }

        var service = serviceType.ToDisplayString();
        var implementationExpression = arguments[implementationArgumentIndex].Expression;

        if (implementationExpression is not TypeOfExpressionSyntax implementationTypeOf)
        {
            return new ParsedRegistrationData(
                service,
                "factory",
                null);
        }

        var implementationType = semanticModel.GetTypeInfo(implementationTypeOf.Type, cancellationToken).Type;

        if (implementationType is null)
        {
            return null;
        }

        return new ParsedRegistrationData(
            service,
            implementationType.ToDisplayString(),
            implementationType as INamedTypeSymbol);
    }

    private static string? GetRegistrationKey(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;

        var keyArgumentIndex = memberAccess.Name is GenericNameSyntax
            ? 0
            : 1;

        if (arguments.Count <= keyArgumentIndex)
        {
            return null;
        }

        var keyExpression = arguments[keyArgumentIndex].Expression;
        var constantValue = semanticModel.GetConstantValue(keyExpression, cancellationToken);

        if (constantValue.HasValue)
        {
            return constantValue.Value?.ToString() ?? "null";
        }

        return keyExpression.ToString();
    }

    private static IReadOnlyList<DiRegistration> AnalyzeRegistrations(IReadOnlyList<ParsedDiRegistration> registrations)
    {
        var duplicateRegistrations = GetDuplicateRegistrations(registrations);
        var scopedServices = GetScopedServices(registrations);

        return registrations
            .Select(registration => CreateAnalyzedRegistration(registration, duplicateRegistrations, scopedServices))
            .ToList();
    }

    private static HashSet<(string Service, string? Key)> GetDuplicateRegistrations(
        IReadOnlyList<ParsedDiRegistration> registrations)
    {
        return registrations
            .GroupBy(x => (x.Service, x.Key))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
    }

    private static HashSet<string> GetScopedServices(IReadOnlyList<ParsedDiRegistration> registrations)
    {
        return registrations
            .Where(x => x.Lifetime == "Scoped" && x.Key is null)
            .Select(x => x.Service)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static DiRegistration CreateAnalyzedRegistration(
        ParsedDiRegistration registration,
        HashSet<(string Service, string? Key)> duplicateRegistrations,
        HashSet<string> scopedServices)
    {
        return new DiRegistration(
            registration.Lifetime,
            registration.Service,
            registration.Implementation,
            registration.Key,
            registration.File,
            registration.Line,
            duplicateRegistrations.Contains((registration.Service, registration.Key)),
            HasCaptiveDependencyRisk(registration, scopedServices));
    }

    private static bool HasCaptiveDependencyRisk(
        ParsedDiRegistration registration,
        HashSet<string> scopedServices)
    {
        if (registration.Lifetime != "Singleton")
        {
            return false;
        }

        if (registration.Implementation == "factory")
        {
            return false;
        }

        if (registration.ImplementationType is null)
        {
            return false;
        }

        var constructor = registration.ImplementationType.InstanceConstructors
            .Where(x => x.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(x => x.Parameters.Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            return false;
        }

        return constructor.Parameters.Any(parameter => scopedServices.Contains(parameter.Type.ToDisplayString()));
    }

    private static string? GetMethodName(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name switch
        {
            GenericNameSyntax genericName => genericName.Identifier.Text,
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            _ => null
        };
    }

    private static string? GetLifetime(string? methodName)
    {
        return methodName switch
        {
            "AddSingleton" or "TryAddSingleton" or "AddKeyedSingleton" => "Singleton",
            "AddScoped" or "TryAddScoped" or "AddKeyedScoped" => "Scoped",
            "AddTransient" or "TryAddTransient" or "AddKeyedTransient" => "Transient",
            _ => null
        };
    }

    private static bool IsKeyedRegistration(string? methodName)
    {
        return methodName is  "AddKeyedSingleton" or "AddKeyedScoped" or   "AddKeyedTransient";
    }

    private sealed record ParsedRegistrationData(string Service, string Implementation, INamedTypeSymbol? ImplementationType);

    private sealed record ParsedDiRegistration( string Lifetime, string Service,  string Implementation,  string? Key, string File,   int Line, INamedTypeSymbol? ImplementationType);

}