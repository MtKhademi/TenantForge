namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// The union of every registered IPermissionCatalogContributor's groups
/// and known keys, computed once at startup (see AggregatedPermissionCatalog).
/// </summary>
public interface IAggregatedPermissionCatalog
{
    IReadOnlyList<PermissionGroup> AllGroups { get; }
    IReadOnlySet<string> AllKnownKeys { get; }
}
