using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNet.RoslynMcp.Endpoints;

public sealed class EndpointMapService(WorkspaceManager workspaceManager)
{
    public async Task<IReadOnlyList<EndpointInfo>> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);
        var solutionDirectory = GetSolutionDirectory(solution);

        var controllerEndpoints = await GetControllerEndpointsAsync(solution, solutionDirectory, cancellationToken);
        var minimalApiEndpoints = await GetMinimalApiEndpointsAsync(solution, solutionDirectory, cancellationToken);

        return [.. controllerEndpoints, .. minimalApiEndpoints];
    }

    private static string GetSolutionDirectory(Solution solution)
    {
        return Path.GetDirectoryName(solution.FilePath) ?? throw new InvalidOperationException("Solution path is not available.");
    }

    private static async Task<IReadOnlyList<EndpointInfo>> GetControllerEndpointsAsync(Solution solution, string solutionDirectory, CancellationToken cancellationToken)
    {
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
                    if (controllerSymbol is null || !IsController(controllerSymbol)) continue;

                    var controllerRoute = GetRoute(controller.AttributeLists);

                    foreach (var method in controller.Members.OfType<MethodDeclarationSyntax>())
                    {
                        var httpMethods = GetHttpMethods(method.AttributeLists);
                        if (httpMethods.Count == 0) continue;

                        var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol;
                        if (methodSymbol is null) continue;

                        var actionRoute = GetRoute(method.AttributeLists);
                        var route = ReplaceRouteTokens(CombineRoutes(controllerRoute, actionRoute), controllerSymbol, methodSymbol);
                        var authorization = GetControllerAuthorization(controllerSymbol, methodSymbol);
                        var lineSpan = method.GetLocation().GetLineSpan();
                        var relativePath = Path.GetRelativePath(solutionDirectory, lineSpan.Path);
                        var controllerName = GetControllerName(controllerSymbol);

                        foreach (var httpMethod in httpMethods)
                        {
                            results.Add(new EndpointInfo(httpMethod, route, methodSymbol.ToDisplayString(), "Controller", controllerName, methodSymbol.Name, authorization, relativePath, lineSpan.StartLinePosition.Line + 1));
                        }
                    }
                }
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<EndpointInfo>> GetMinimalApiEndpointsAsync(Solution solution, string solutionDirectory, CancellationToken cancellationToken)
    {
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

                    var httpMethod = GetMinimalApiHttpMethod(methodSymbol.Name);
                    if (httpMethod is null) continue;

                    var arguments = invocation.ArgumentList.Arguments;
                    if (arguments.Count == 0) continue;

                    var routeExpression = arguments[0].Expression;
                    var constantValue = semanticModel.GetConstantValue(routeExpression, cancellationToken);
                    if (!constantValue.HasValue || constantValue.Value is not string route) continue;

                    RouteGroupInfo? routeGroup = null;

                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax identifier && routeGroups.TryGetValue(identifier.Identifier.Text, out var group))
                    {
                        routeGroup = group;
                        route = CombineRoutes(group.Route, route);
                    }

                    var handler = GetMinimalApiHandler(invocation, semanticModel, cancellationToken);
                    var endpointAuthorization = GetMinimalApiAuthorization(invocation);
                    var authorization = CombineAuthorization(routeGroup?.Authorization, endpointAuthorization);
                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    var relativePath = Path.GetRelativePath(solutionDirectory, lineSpan.Path);

                    results.Add(new EndpointInfo(httpMethod, route, handler, "MinimalApi", null, null, authorization, relativePath, lineSpan.StartLinePosition.Line + 1));
                }
            }
        }

        return results;
    }

    private static bool IsController(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name is "ControllerBase" or "Controller") return true;
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

    private static string? GetMinimalApiHttpMethod(string methodName)
    {
        return methodName switch
        {
            "MapGet" => "GET",
            "MapPost" => "POST",
            "MapPut" => "PUT",
            "MapDelete" => "DELETE",
            "MapPatch" => "PATCH",
            _ => null
        };
    }

    private static string GetRoute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attribute in attributeLists.SelectMany(list => list.Attributes))
        {
            var name = attribute.Name.ToString();

            if (!name.EndsWith("Route", StringComparison.Ordinal) && !name.StartsWith("Http", StringComparison.Ordinal)) continue;
            if (attribute.ArgumentList?.Arguments.Count is not > 0) continue;

            var expression = attribute.ArgumentList.Arguments[0].Expression;

            if (expression is LiteralExpressionSyntax literal)
            {
                return literal.Token.ValueText;
            }
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
        return route.Replace("[controller]", GetControllerName(controllerSymbol), StringComparison.OrdinalIgnoreCase).Replace("[action]", methodSymbol.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetControllerName(INamedTypeSymbol controllerSymbol)
    {
        return controllerSymbol.Name.EndsWith("Controller", StringComparison.Ordinal) ? controllerSymbol.Name[..^"Controller".Length] : controllerSymbol.Name;
    }

    private static string GetControllerAuthorization(INamedTypeSymbol controllerSymbol, IMethodSymbol methodSymbol)
    {
        var controllerAttributes = controllerSymbol.GetAttributes();
        var methodAttributes = methodSymbol.GetAttributes();

        if (HasAttribute(methodAttributes, "AllowAnonymousAttribute") || HasAttribute(controllerAttributes, "AllowAnonymousAttribute"))
        {
            return "Anonymous";
        }

        if (HasAttribute(methodAttributes, "AuthorizeAttribute") || HasAttribute(controllerAttributes, "AuthorizeAttribute"))
        {
            return "Authorized";
        }

        return "Unmarked";
    }

    private static bool HasAttribute(IEnumerable<AttributeData> attributes, string attributeName)
    {
        return attributes.Any(attribute => attribute.AttributeClass?.Name == attributeName);
    }

    private static string GetMinimalApiHandler(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count <= 1)
        {
            return "lambda expression";
        }

        var handlerExpression = invocation.ArgumentList.Arguments[1].Expression;
        var handlerSymbolInfo = semanticModel.GetSymbolInfo(handlerExpression, cancellationToken);
        var handlerMethod = handlerSymbolInfo.Symbol as IMethodSymbol ?? handlerSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        return handlerMethod?.ToDisplayString() ?? "lambda expression";
    }

    private static string GetMinimalApiAuthorization(InvocationExpressionSyntax endpointInvocation)
    {
        var current = endpointInvocation;

        while (current.Parent is MemberAccessExpressionSyntax memberAccess && ReferenceEquals(memberAccess.Expression, current) && memberAccess.Parent is InvocationExpressionSyntax chainedInvocation)
        {
            var methodName = memberAccess.Name.Identifier.Text;

            if (methodName == "AllowAnonymous")
            {
                return "Anonymous";
            }

            if (methodName == "RequireAuthorization")
            {
                return "Authorized";
            }

            current = chainedInvocation;
        }

        return "Unmarked";
    }

    private static string CombineAuthorization(string? groupAuthorization, string endpointAuthorization)
    {
        if (endpointAuthorization == "Anonymous") return "Anonymous";
        if (endpointAuthorization == "Authorized") return "Authorized";

        return groupAuthorization ?? "Unmarked";
    }

    private static Dictionary<string, RouteGroupInfo> GetRouteGroups(SyntaxNode root, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, RouteGroupInfo>(StringComparer.Ordinal);
        var unresolved = new List<UnresolvedRouteGroup>();

        foreach (var declaration in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.Initializer?.Value is not InvocationExpressionSyntax initializer) continue;

            var mapGroupInvocation = FindMapGroupInvocation(initializer, semanticModel, cancellationToken);
            if (mapGroupInvocation is null || mapGroupInvocation.ArgumentList.Arguments.Count == 0) continue;

            var routeExpression = mapGroupInvocation.ArgumentList.Arguments[0].Expression;
            var constantValue = semanticModel.GetConstantValue(routeExpression, cancellationToken);
            if (!constantValue.HasValue || constantValue.Value is not string route) continue;

            string? parent = null;

            if (mapGroupInvocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text != "app")
            {
                parent = identifier.Identifier.Text;
            }

            var authorization = GetMinimalApiAuthorization(mapGroupInvocation);

            unresolved.Add(new UnresolvedRouteGroup(declaration.Identifier.Text, parent, route, authorization));
        }

        ResolveRouteGroups(unresolved, groups);
        ApplyStandaloneGroupAuthorization(root, groups);

        return groups;
    }

    private static InvocationExpressionSyntax? FindMapGroupInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        InvocationExpressionSyntax? current = invocation;

        while (current is not null)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(current, cancellationToken);

            if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.Name == "MapGroup")
            {
                return current;
            }

            if (current.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is InvocationExpressionSyntax innerInvocation)
            {
                current = innerInvocation;
                continue;
            }

            break;
        }

        return null;
    }

    private static void ResolveRouteGroups(List<UnresolvedRouteGroup> unresolved, Dictionary<string, RouteGroupInfo> groups)
    {
        var changed = true;

        while (changed && unresolved.Count > 0)
        {
            changed = false;

            for (var i = unresolved.Count - 1; i >= 0; i--)
            {
                var item = unresolved[i];

                if (item.Parent is null)
                {
                    groups[item.Name] = new RouteGroupInfo(item.Route, item.Authorization);
                    unresolved.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (!groups.TryGetValue(item.Parent, out var parentGroup)) continue;

                var authorization = CombineAuthorization(parentGroup.Authorization, item.Authorization);
                groups[item.Name] = new RouteGroupInfo(CombineRoutes(parentGroup.Route, item.Route), authorization);
                unresolved.RemoveAt(i);
                changed = true;
            }
        }
    }

    private static void ApplyStandaloneGroupAuthorization(SyntaxNode root, Dictionary<string, RouteGroupInfo> groups)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;
            if (memberAccess.Expression is not IdentifierNameSyntax identifier) continue;
            if (!groups.TryGetValue(identifier.Identifier.Text, out var group)) continue;

            var methodName = memberAccess.Name.Identifier.Text;

            if (methodName == "AllowAnonymous")
            {
                groups[identifier.Identifier.Text] = group with { Authorization = "Anonymous" };
            }
            else if (methodName == "RequireAuthorization")
            {
                groups[identifier.Identifier.Text] = group with { Authorization = "Authorized" };
            }
        }
    }

    private sealed record RouteGroupInfo(string Route, string Authorization);

    private sealed record UnresolvedRouteGroup(string Name, string? Parent, string Route, string Authorization);
}