using System.Xml.Linq;

namespace MedResearch.Application.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] DomainForbiddenTokens =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "Npgsql",
        "MedResearch.Infrastructure",
        "MedResearch.Api"
    ];

    private static readonly string[] ApplicationForbiddenTokens =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "Npgsql",
        "MedResearch.Infrastructure",
        "MedResearch.Api"
    ];

    [Fact]
    public void DomainProject_HasNoInfrastructureOrApplicationDependencies()
    {
        var root = FindRepositoryRoot();
        var project = LoadProject(root, "src", "MedResearch.Domain", "MedResearch.Domain.csproj");

        Assert.Empty(GetProjectReferences(project));
        Assert.Empty(FindSourceFilesContaining(root, Path.Combine("src", "MedResearch.Domain"), DomainForbiddenTokens));
    }

    [Fact]
    public void ApplicationProject_DependsOnlyOnDomainWithinMedResearch()
    {
        var root = FindRepositoryRoot();
        var project = LoadProject(root, "src", "MedResearch.Application", "MedResearch.Application.csproj");
        var medResearchReferences = GetProjectReferences(project)
            .Where(reference => reference.Contains("MedResearch.", StringComparison.Ordinal))
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Replace('\\', Path.DirectorySeparatorChar)))
            .Order()
            .ToArray();

        Assert.Equal(["MedResearch.Domain"], medResearchReferences);
        Assert.Empty(FindSourceFilesContaining(root, Path.Combine("src", "MedResearch.Application"), ApplicationForbiddenTokens));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MedResearch.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate MedResearch repository root.");
    }

    private static XDocument LoadProject(string root, params string[] pathParts)
    {
        return XDocument.Load(Path.Combine([root, .. pathParts]));
    }

    private static string[] GetProjectReferences(XDocument project)
    {
        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static string[] FindSourceFilesContaining(string root, string relativeDirectory, IReadOnlyCollection<string> forbiddenTokens)
    {
        var sourceRoot = Path.Combine(root, relativeDirectory);

        return Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(sourceRoot, file))
            .Where(file => !file.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal))
            .Select(file => new
            {
                File = Path.GetRelativePath(root, file),
                Text = File.ReadAllText(file)
            })
            .Where(file => forbiddenTokens.Any(token => file.Text.Contains(token, StringComparison.Ordinal)))
            .Select(file => file.File)
            .Order()
            .ToArray();
    }

    private static bool IsBuildOutput(string sourceRoot, string file)
    {
        var relativeParts = Path.GetRelativePath(sourceRoot, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return relativeParts.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
               relativeParts.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }
}
