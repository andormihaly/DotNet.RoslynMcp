using DotNet.RoslynMcp.DependencyInjection;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class DependencyInjectionTools(DiRegistrationService diRegistrationService)
{
    [McpServerTool, Description("Returns dependency injection registrations in the loaded .NET solution.")]
    public async Task<string> GetDiRegistrations(CancellationToken cancellationToken = default)
    {
        var registrations = await diRegistrationService.GetRegistrationsAsync(cancellationToken);
        if (registrations.Count == 0) return "No dependency injection registrations found.";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            registrations.Select(registration =>
                $"Lifetime: {registration.Lifetime}{Environment.NewLine}" +
                $"Service: {registration.Service}{Environment.NewLine}" +
                $"Implementation: {registration.Implementation}{Environment.NewLine}" +
                $"File: {registration.File}{Environment.NewLine}" +
                $"Line: {registration.Line}"));
    }
}