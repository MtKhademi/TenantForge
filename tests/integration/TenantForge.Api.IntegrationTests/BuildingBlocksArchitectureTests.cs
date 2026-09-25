using System.Reflection;
using System.Xml.Linq;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Iam;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

public sealed class BuildingBlocksArchitectureTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact]
    public void MovedContracts_LiveInBuildingBlocksAssembly()
    {
        Assert.Equal("TenantForge.BuildingBlocks", typeof(IModuleConfig).Assembly.GetName().Name);
        Assert.Equal("TenantForge.BuildingBlocks", typeof(TsidId).Assembly.GetName().Name);
        Assert.True(typeof(IModuleConfig).IsAssignableFrom(typeof(IAMConfig)));
    }

    [Fact]
    public void BuildingBlocks_ExportsOnlyTheApprovedProductionTypes()
    {
        var exportedTypes = typeof(IModuleConfig).Assembly
            .GetExportedTypes()
            .Where(type => !IsCompilerGenerated(type))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([
            "TenantForge.BuildingBlocks.Identifiers.TsidId",
            "TenantForge.BuildingBlocks.Modules.IModuleConfig",
            "TenantForge.BuildingBlocks.Permissions.AggregatedPermissionCatalog",
            "TenantForge.BuildingBlocks.Permissions.IAggregatedPermissionCatalog",
            "TenantForge.BuildingBlocks.Permissions.IPermissionCatalogContributor",
            "TenantForge.BuildingBlocks.Permissions.PermissionDescriptor",
            "TenantForge.BuildingBlocks.Permissions.PermissionGroup"
        ], exportedTypes);
    }

    [Fact]
    public void BuildingBlocks_DoesNotReferenceApiModulesEfOrNpgsql()
    {
        var referencedAssemblies = typeof(IModuleConfig).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("TenantForge.Modules.", StringComparison.Ordinal));
        Assert.DoesNotContain("TenantForge.Api", referencedAssemblies);
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectReferences_FlowFromApiToIamToBuildingBlocksOnly()
    {
        var iamReferences = ProjectReferences("src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj");
        var apiReferences = ProjectReferences("src/api/TenantForge.Api/TenantForge.Api.csproj");
        var buildingBlocksReferences = ProjectReferences("src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj");

        Assert.Contains(iamReferences, reference => reference.EndsWith("src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(apiReferences, reference => reference.EndsWith("src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj", StringComparison.Ordinal));
        Assert.Empty(buildingBlocksReferences);
    }

    private static string[] ProjectReferences(string repositoryRelativeProjectPath)
    {
        var projectPath = Path.Combine(RepositoryRoot, repositoryRelativeProjectPath);
        var project = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        return project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!.Replace('\\', Path.DirectorySeparatorChar))))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCompilerGenerated(Type type)
        => type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TenantForge.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate TenantForge.sln from the test output directory.");
    }
}
