using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNet.RoslynMcp.Endpoints;

public sealed class EndpointMapService(WorkspaceManager workspaceManager)
{
    public async Task<IReadOnlyList<EndpointInfo>> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var controllerEndpoints = await GetEndpointsCoreAsync(cancellationToken);
        var minimalApiEndpoints = await GetMinimalApiEndpointsAsync(cancellationToken);

        return [.. controllerEndpoints, .. minimalApiEndpoints];
    }

    private async Task<IReadOnlyList<EndpointInfo>> GetEndpointsCoreAsync(CancellationToken cancellationToken)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var results = new List<EndpointInfo>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null) continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel is null) continue;

                foreach (var controller in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var controllerSymbol = semanticModel.GetDeclaredSymbol(controller, cancellationToken) as INamedTypeSymbol;
                    if (controllerSymbol is null) continue;

                    if (!IsController(controllerSymbol)) continue;

                    var controllerRoute = GetRoute(controller.AttributeLists);

                    foreach (var method in controller.Members.OfType<MethodDeclarationSyntax>())
                    {
                        var httpMethods = GetHttpMethods(method.AttributeLists);
                        if (httpMethods.Count == 0) continue;

                        var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol;
                        if (methodSymbol is null) continue;

                        var actionRoute = GetRoute(method.AttributeLists);
                        var route = CombineRoutes(controllerRoute, actionRoute);
                        route = ReplaceRouteTokens(route, controllerSymbol, methodSymbol);

                        var lineSpan = method.GetLocation().GetLineSpan();

                        foreach (var httpMethod in httpMethods)
                        {
                            results.Add(new EndpointInfo(httpMethod, route, methodSymbol.ToDisplayString(), lineSpan.Path, lineSpan.StartLinePosition.Line + 1));
                        }
                    }
                }
            }
        }

        return results;
    }

    private static bool IsController(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "ControllerBase" || current.Name == "Controller") return true;
        }

        return false;
    }
    private static IReadOnlyList<string> GetHttpMethods(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var methods = new List<string>();

        foreach (var attribute in attributeLists.SelectMany(list => list.Attributes))
        {
            var name = attribute.Name.ToString();

            if (name.EndsWith("HttpGet", StringComparison.Ordinal)) methods.Add("GET");
            else if (name.EndsWith("HttpPost", StringComparison.Ordinal)) methods.Add("POST");
            else if (name.EndsWith("HttpPut", StringComparison.Ordinal)) methods.Add("PUT");
            else if (name.EndsWith("HttpDelete", StringComparison.Ordinal)) methods.Add("DELETE");
            else if (name.EndsWith("HttpPatch", StringComparison.Ordinal)) methods.Add("PATCH");
        }

        return methods;
    }

    private static string GetRoute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attribute in attributeLists.SelectMany(list => list.Attributes))
        {
            var name = attribute.Name.ToString();

            if (!name.EndsWith("Route", StringComparison.Ordinal) && !name.StartsWith("Http", StringComparison.Ordinal)) continue;
            if (attribute.ArgumentList?.Arguments.Count is not > 0) continue;

            var expression = attribute.ArgumentList.Arguments[0].Expression;

            if (expression is LiteralExpressionSyntax literal && literal.Token.ValueText is { } value) return value;
        }

        return string.Empty;
    }

    private static string CombineRoutes(string controllerRoute, string actionRoute)
    {
        var controller = controllerRoute.Trim('/');
        var action = actionRoute.Trim('/');

        if (string.IsNullOrWhiteSpace(controller)) return string.IsNullOrWhiteSpace(action) ? "/" : $"/{action}";
        if (string.IsNullOrWhiteSpace(action)) return $"/{controller}";

        return $"/{controller}/{action}";
    }
    private static string ReplaceRouteTokens(string route, INamedTypeSymbol controllerSymbol, IMethodSymbol methodSymbol)
    {
        var controllerName = controllerSymbol.Name.EndsWith("Controller", StringComparison.Ordinal) ? controllerSymbol.Name[..^"Controller".Length] : controllerSymbol.Name;
        return route.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase).Replace("[action]", methodSymbol.Name, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<EndpointInfo>> GetMinimalApiEndpointsAsync(CancellationToken cancellationToken)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var results = new List<EndpointInfo>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null) continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel is null) continue;

                var routeGroups = GetRouteGroups(root, semanticModel, cancellationToken);

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var httpMethod = methodSymbol.Name switch
                    {
                        "MapGet" => "GET",
                        "MapPost" => "POST",
                        "MapPut" => "PUT",
                        "MapDelete" => "DELETE",
                        "MapPatch" => "PATCH",
                        _ => null
                    };

                    if (httpMethod is null) continue;
                    if (invocation.ArgumentList.Arguments.Count == 0) continue;

                    var routeExpression = invocation.ArgumentList.Arguments[0].Expression;
                    var constantValue = semanticModel.GetConstantValue(routeExpression, cancellationToken);
                    if (!constantValue.HasValue || constantValue.Value is not string route) continue;

                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax identifier && routeGroups.TryGetValue(identifier.Identifier.Text, out var groupRoute)) route = CombineRoutes(groupRoute, route);

                    var handler = "lambda expression";

                    if (invocation.ArgumentList.Arguments.Count > 1)
                    {
                        var handlerExpression = invocation.ArgumentList.Arguments[1].Expression;
                        var handlerSymbolInfo = semanticModel.GetSymbolInfo(handlerExpression, cancellationToken);
                        var handlerMethod = handlerSymbolInfo.Symbol as IMethodSymbol ?? handlerSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                        if (handlerMethod is not null) handler = handlerMethod.ToDisplayString();
                    }

                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    results.Add(new EndpointInfo(httpMethod, route, handler, lineSpan.Path, lineSpan.StartLinePosition.Line + 1));
                }
            }
        }

        return results;
    }
    private static Dictionary<string, string> GetRouteGroups(SyntaxNode root, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
        var unresolved = new List<(string Name, string? Parent, string Route)>();

        foreach (var declaration in declarations)
        {
            if (declaration.Initializer?.Value is not InvocationExpressionSyntax invocation) continue;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol || methodSymbol.Name != "MapGroup") continue;
            if (invocation.ArgumentList.Arguments.Count == 0) continue;

            var routeExpression = invocation.ArgumentList.Arguments[0].Expression;
            var constantValue = semanticModel.GetConstantValue(routeExpression, cancellationToken);
            if (!constantValue.HasValue || constantValue.Value is not string route) continue;

            string? parent = null;

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text != "app")
            {
                parent = identifier.Identifier.Text;
            }

            unresolved.Add((declaration.Identifier.Text, parent, route));
        }

        var changed = true;

        while (changed && unresolved.Count > 0)
        {
            changed = false;

            for (var i = unresolved.Count - 1; i >= 0; i--)
            {
                var item = unresolved[i];

                if (item.Parent is null)
                {
                    groups[item.Name] = item.Route;
                    unresolved.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (!groups.TryGetValue(item.Parent, out var parentRoute)) continue;

                groups[item.Name] = CombineRoutes(parentRoute, item.Route);
                unresolved.RemoveAt(i);
                changed = true;
            }
        }

        return groups;
    }
}