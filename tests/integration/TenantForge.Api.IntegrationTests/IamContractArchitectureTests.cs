using System.Reflection;
using System.Xml.Linq;
using TenantForge.Modules.Iam.Contract.Requests;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

public sealed class IamContractArchitectureTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact]
    public void IamContract_ProjectHasZeroProjectReferences()
    {
        var contractReferences = ProjectReferences("src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj");

        Assert.Empty(contractReferences);
    }

    [Fact]
    public void IamContract_AssemblyDoesNotReferenceApiModulesEfOrNpgsql()
    {
        var referencedAssemblies = typeof(LoginRequest).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("TenantForge.Modules.", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("TenantForge.Api", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public void IamModule_ReferencesContractExactlyOnce()
    {
        var iamReferences = ProjectReferences("src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj");

        var contractReferences = iamReferences
            .Where(reference => reference.EndsWith("src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj", StringComparison.Ordinal))
            .ToArray();

        // Exactly one: the Contract reference. (IAM's other ProjectReference is
        // to TenantForge.BuildingBlocks, which this filter excludes by design.)
        Assert.Single(contractReferences);
    }

    [Fact]
    public void IamContract_ExportsOnlyTheApprovedProductionTypes()
    {
        var exportedTypes = typeof(LoginRequest).Assembly
            .GetExportedTypes()
            .Where(type => !IsCompilerGenerated(type))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // B023/S23: the full 32-type roster of tasks/slices/023-iam-contract-separation.md,
        // hand-enumerated (never computed). The slice lists it in folder-convention
        // order; this assertion compares ordinally, where "Queries" < "Requests".
        Assert.Equal([
            "TenantForge.Modules.Iam.Contract.Queries.PaginationQuery",
            "TenantForge.Modules.Iam.Contract.Requests.CreateInvitationRequest",
            "TenantForge.Modules.Iam.Contract.Requests.CreateRoleRequest",
            "TenantForge.Modules.Iam.Contract.Requests.CreateTenantRequest",
            "TenantForge.Modules.Iam.Contract.Requests.CreateUserRequest",
            "TenantForge.Modules.Iam.Contract.Requests.LoginRequest",
            "TenantForge.Modules.Iam.Contract.Requests.UpdateRoleRequest",
            "TenantForge.Modules.Iam.Contract.Responses.AuditEventResponse",
            "TenantForge.Modules.Iam.Contract.Responses.AuditListResponse",
            "TenantForge.Modules.Iam.Contract.Responses.CurrentAccountResponse",
            "TenantForge.Modules.Iam.Contract.Responses.DashboardSummaryResponse",
            "TenantForge.Modules.Iam.Contract.Responses.DiscoveredTenantResponse",
            "TenantForge.Modules.Iam.Contract.Responses.InvitationListResponse",
            "TenantForge.Modules.Iam.Contract.Responses.InvitationResponse",
            "TenantForge.Modules.Iam.Contract.Responses.LoginResponse",
            "TenantForge.Modules.Iam.Contract.Responses.LoginUserResponse",
            "TenantForge.Modules.Iam.Contract.Responses.PagedTenantRolesResponse",
            "TenantForge.Modules.Iam.Contract.Responses.PaginationMetadata",
            "TenantForge.Modules.Iam.Contract.Responses.PermissionCatalogResponse",
            "TenantForge.Modules.Iam.Contract.Responses.PermissionGroupResponse",
            "TenantForge.Modules.Iam.Contract.Responses.PermissionResponse",
            "TenantForge.Modules.Iam.Contract.Responses.ResolvedPermissionsResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantContextResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantDiscoveryResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantListResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantMemberResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantMembersResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantRoleResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantRolesResponse",
            "TenantForge.Modules.Iam.Contract.Responses.TenantSummaryResponse",
            "TenantForge.Modules.Iam.Contract.Responses.UserResponse",
            "TenantForge.Modules.Iam.Contract.Responses.UsersListResponse"
        ], exportedTypes);
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
