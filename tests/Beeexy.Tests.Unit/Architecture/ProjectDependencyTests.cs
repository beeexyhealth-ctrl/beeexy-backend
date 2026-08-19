using System.Xml.Linq;

namespace Beeexy.Tests.Unit.Architecture;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void Domain_DependsOnNoBeeexyProjectOrExternalPackage()
    {
        AssertProjectReferences("src/Beeexy.Domain/Beeexy.Domain.csproj");
        AssertPackageReferences("src/Beeexy.Domain/Beeexy.Domain.csproj");
    }

    [Fact]
    public void Application_DependsOnlyOnDomain()
    {
        AssertProjectReferences(
            "src/Beeexy.Application/Beeexy.Application.csproj",
            "Beeexy.Domain");
    }

    [Fact]
    public void Infrastructure_DependsOnlyOnApplicationAndDomain()
    {
        AssertProjectReferences(
            "src/Beeexy.Infrastructure/Beeexy.Infrastructure.csproj",
            "Beeexy.Application",
            "Beeexy.Domain");
    }

    [Fact]
    public void Api_ComposesOnlyApplicationAndInfrastructure()
    {
        AssertProjectReferences(
            "src/Beeexy.Api/Beeexy.Api.csproj",
            "Beeexy.Application",
            "Beeexy.Infrastructure");
    }

    private static void AssertProjectReferences(string projectPath, params string[] expectedProjects)
    {
        var project = LoadProject(projectPath);
        var actualProjects = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => Path.GetFileNameWithoutExtension(reference!))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProjects.OrderBy(name => name, StringComparer.Ordinal), actualProjects);
    }

    private static void AssertPackageReferences(string projectPath, params string[] expectedPackages)
    {
        var project = LoadProject(projectPath);
        var actualPackages = project
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedPackages.OrderBy(name => name, StringComparer.Ordinal), actualPackages);
    }

    private static XDocument LoadProject(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        return XDocument.Load(Path.Combine(repositoryRoot, relativePath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beeexy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Beeexy solution root.");
    }
}
