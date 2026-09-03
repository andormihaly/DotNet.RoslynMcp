using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DotNet.RoslynMcp.Projects
{

    public sealed class ProjectFileReader
    {
        public async Task<ProjectFileInfo> ReadProjectFileInfoAsync(string projectFilePath, string solutionDirectory, CancellationToken cancellationToken)
        {
            var projectDirectory = Path.GetDirectoryName(projectFilePath)
                ?? throw new InvalidOperationException("Project directory is not available.");

            var projectDocument = await LoadXmlAsync(projectFilePath, cancellationToken);

            var directoryBuildPropsPath = FindFileUpwards(projectDirectory, solutionDirectory, "Directory.Build.props");
            var directoryPackagesPropsPath = FindFileUpwards(projectDirectory, solutionDirectory, "Directory.Packages.props");

            XDocument? directoryBuildPropsDocument = null;
            XDocument? directoryPackagesPropsDocument = null;

            if (directoryBuildPropsPath is not null)
            {
                directoryBuildPropsDocument = await LoadXmlAsync(directoryBuildPropsPath, cancellationToken);
            }

            if (directoryPackagesPropsPath is not null)
            {
                directoryPackagesPropsDocument = await LoadXmlAsync(directoryPackagesPropsPath, cancellationToken);
            }

            var targetFramework =
                GetProjectProperty(projectDocument, "TargetFramework") ??
                GetProjectProperty(projectDocument, "TargetFrameworks") ??
                GetProjectProperty(directoryBuildPropsDocument, "TargetFramework") ??
                GetProjectProperty(directoryBuildPropsDocument, "TargetFrameworks");

            var nullable =
                GetProjectProperty(projectDocument, "Nullable") ??
                GetProjectProperty(directoryBuildPropsDocument, "Nullable");

            var implicitUsings =
                GetProjectProperty(projectDocument, "ImplicitUsings") ??
                GetProjectProperty(directoryBuildPropsDocument, "ImplicitUsings");

            var centralPackageVersions = GetCentralPackageVersions(directoryPackagesPropsDocument);
            var packageReferences = GetPackageReferences(projectDocument, centralPackageVersions);

            return new ProjectFileInfo(targetFramework, nullable, implicitUsings, packageReferences);
        }

        private static async Task<XDocument> LoadXmlAsync(string path, CancellationToken cancellationToken)
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return XDocument.Parse(content);
        }

        private static string? FindFileUpwards(string startDirectory, string stopDirectory, string fileName)
        {
            var currentDirectory = new DirectoryInfo(startDirectory);
            var normalizedStopDirectory = Path.GetFullPath(stopDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            while (currentDirectory is not null)
            {
                var candidate = Path.Combine(currentDirectory.FullName, fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                var normalizedCurrentDirectory = Path.GetFullPath(currentDirectory.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(normalizedCurrentDirectory, normalizedStopDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                currentDirectory = currentDirectory.Parent;
            }

            return null;
        }

        private static string? GetProjectProperty(XDocument? document, string propertyName)
        {
            if (document?.Root is null)
            {
                return null;
            }

            return document.Root
                .Elements()
                .Where(element => element.Name.LocalName == "PropertyGroup")
                .SelectMany(group => group.Elements())
                .FirstOrDefault(element => element.Name.LocalName == propertyName)?
                .Value;
        }

        private static Dictionary<string, string> GetCentralPackageVersions(XDocument? document)
        {
            if (document?.Root is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return document.Root
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageVersion")
                .Select(element => new
                {
                    Name = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value,
                    Version = element.Attribute("Version")?.Value ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value
                })
                .Where(package => !string.IsNullOrWhiteSpace(package.Name) && !string.IsNullOrWhiteSpace(package.Version))
                .ToDictionary(package => package.Name!, package => package.Version!, StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<PackageReferenceInfo> GetPackageReferences(XDocument projectDocument, IReadOnlyDictionary<string, string> centralPackageVersions)
        {
            return projectDocument.Root?
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element =>
                {
                    var name = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return null;
                    }

                    var version =
                        element.Attribute("Version")?.Value ??
                        element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value;

                    if (string.IsNullOrWhiteSpace(version) && centralPackageVersions.TryGetValue(name, out var centralVersion))
                    {
                        version = centralVersion;
                    }

                    return new PackageReferenceInfo(name, version);
                })
                .Where(package => package is not null)
                .Select(package => package!)
                .ToList() ?? [];
        }
    }
}
