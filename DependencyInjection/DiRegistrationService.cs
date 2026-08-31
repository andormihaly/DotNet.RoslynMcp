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
        var results = new List<DiRegistration>();

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
                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

                    var methodName = memberAccess.Name switch
                    {
                        GenericNameSyntax genericName => genericName.Identifier.Text,
                        IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
                        _ => null
                    };

                    var lifetime = methodName switch
                    {
                        "AddSingleton" => "Singleton",
                        "AddScoped" => "Scoped",
                        "AddTransient" => "Transient",
                        _ => null
                    };

                    if (lifetime is null) continue;

                    string service;
                    string implementation;

                    if (memberAccess.Name is GenericNameSyntax registrationGenericName)
                    {
                        var typeArguments = registrationGenericName.TypeArgumentList.Arguments;
                        if (typeArguments.Count is < 1 or > 2) continue;

                        var serviceType = semanticModel.GetTypeInfo(typeArguments[0], cancellationToken).Type;
                        if (serviceType is null) continue;

                        service = serviceType.ToDisplayString();

                        if (typeArguments.Count == 2)
                        {
                            var implementationType = semanticModel.GetTypeInfo(typeArguments[1], cancellationToken).Type;
                            if (implementationType is null) continue;

                            implementation = implementationType.ToDisplayString();
                        }
                        else if (invocation.ArgumentList.Arguments.Count > 0)
                        {
                            implementation = "factory";
                        }
                        else
                        {
                            implementation = service;
                        }
                    }
                    else
                    {
                        if (invocation.ArgumentList.Arguments.Count < 2) continue;

                        var serviceExpression = invocation.ArgumentList.Arguments[0].Expression;
                        var implementationExpression = invocation.ArgumentList.Arguments[1].Expression;

                        if (serviceExpression is not TypeOfExpressionSyntax serviceTypeOf) continue;
                        if (implementationExpression is not TypeOfExpressionSyntax implementationTypeOf) continue;

                        var serviceType = semanticModel.GetTypeInfo(serviceTypeOf.Type, cancellationToken).Type;
                        var implementationType = semanticModel.GetTypeInfo(implementationTypeOf.Type, cancellationToken).Type;
                        if (serviceType is null || implementationType is null) continue;

                        service = serviceType.ToDisplayString();
                        implementation = implementationType.ToDisplayString();
                    }

                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    results.Add(new DiRegistration(lifetime, service, implementation, lineSpan.Path, lineSpan.StartLinePosition.Line + 1));
                }
            }
        }

        return results;
    }
}