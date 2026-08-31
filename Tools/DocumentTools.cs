using DotNet.RoslynMcp.Workspace;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNet.RoslynMcp.Tools;

[McpServerToolType]
public sealed class DocumentTools(WorkspaceManager workspaceManager)
{
    [McpServerTool, Description("Returns a structural outline of a C# file in the loaded .NET solution.")]
    public async Task<string> GetFileOutline(string file, CancellationToken cancellationToken = default)
    {
        var solution = await workspaceManager.GetSolutionAsync(cancellationToken);

        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document =>
                !string.IsNullOrWhiteSpace(document.FilePath) &&
                string.Equals(document.FilePath, file, StringComparison.OrdinalIgnoreCase));

        if (document is null)
        {
            return $"File not found: {file}";
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);

        if (root is null)
        {
            return $"Syntax tree not available: {file}";
        }

        var lines = new List<string>
        {
            $"File: {document.FilePath}"
        };

        foreach (var member in root.ChildNodes().OfType<MemberDeclarationSyntax>())
        {
            AddMember(lines, member, 0);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddMember(List<string> lines, MemberDeclarationSyntax member, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);

        switch (member)
        {
            case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                lines.Add(string.Empty);
                lines.Add($"{indent}namespace {namespaceDeclaration.Name}");

                foreach (var child in namespaceDeclaration.Members)
                {
                    AddMember(lines, child, indentLevel + 1);
                }

                break;

            case ClassDeclarationSyntax classDeclaration:
                lines.Add(string.Empty);
                lines.Add($"{indent}class {classDeclaration.Identifier.Text}");

                foreach (var child in classDeclaration.Members)
                {
                    AddMember(lines, child, indentLevel + 1);
                }

                break;

            case InterfaceDeclarationSyntax interfaceDeclaration:
                lines.Add(string.Empty);
                lines.Add($"{indent}interface {interfaceDeclaration.Identifier.Text}");

                foreach (var child in interfaceDeclaration.Members)
                {
                    AddMember(lines, child, indentLevel + 1);
                }

                break;

            case StructDeclarationSyntax structDeclaration:
                lines.Add(string.Empty);
                lines.Add($"{indent}struct {structDeclaration.Identifier.Text}");

                foreach (var child in structDeclaration.Members)
                {
                    AddMember(lines, child, indentLevel + 1);
                }

                break;

            case RecordDeclarationSyntax recordDeclaration:
                lines.Add(string.Empty);
                lines.Add($"{indent}record {recordDeclaration.Identifier.Text}");

                foreach (var child in recordDeclaration.Members)
                {
                    AddMember(lines, child, indentLevel + 1);
                }

                break;

            case EnumDeclarationSyntax enumDeclaration:
                lines.Add(string.Empty);
                lines.Add($"{indent}enum {enumDeclaration.Identifier.Text}");

                foreach (var enumMember in enumDeclaration.Members)
                {
                    lines.Add($"{indent}  value {enumMember.Identifier.Text}");
                }

                break;

            case ConstructorDeclarationSyntax constructorDeclaration:
                lines.Add($"{indent}constructor {constructorDeclaration.Identifier.Text}{constructorDeclaration.ParameterList}");
                break;

            case MethodDeclarationSyntax methodDeclaration:
                lines.Add($"{indent}method {methodDeclaration.Identifier.Text}{methodDeclaration.ParameterList}");
                break;

            case PropertyDeclarationSyntax propertyDeclaration:
                lines.Add($"{indent}property {propertyDeclaration.Identifier.Text}");
                break;

            case FieldDeclarationSyntax fieldDeclaration:
                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    lines.Add($"{indent}field {variable.Identifier.Text}");
                }

                break;

            case EventDeclarationSyntax eventDeclaration:
                lines.Add($"{indent}event {eventDeclaration.Identifier.Text}");
                break;

            case EventFieldDeclarationSyntax eventFieldDeclaration:
                foreach (var variable in eventFieldDeclaration.Declaration.Variables)
                {
                    lines.Add($"{indent}event {variable.Identifier.Text}");
                }

                break;
        }
    }
}