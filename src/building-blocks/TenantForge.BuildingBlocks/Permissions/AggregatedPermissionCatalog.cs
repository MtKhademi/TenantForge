namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// The one, trivial implementation of IAggregatedPermissionCatalog.
/// Flattens every registered contributor's groups and keys exactly once,
/// in the constructor — module contributors are fixed at startup
/// (registered as singletons), so there is no per-call recomputation.
/// </summary>
public sealed class AggregatedPermissionCatalog : IAggregatedPermissionCatalog
{
    public IReadOnlyList<PermissionGroup> AllGroups { get; }
    public IReadOnlySet<string> AllKnownKeys { get; }

    public AggregatedPermissionCatalog(IEnumerable<IPermissionCatalogContributor> contributors)
    {
        var groups = new List<PermissionGroup>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contributor in contributors)
        {
            foreach (var group in contributor.GetPermissionGroups())
            {
                groups.Add(group);
                foreach (var permission in group.Permissions)
                {
                    keys.Add(permission.Key);
                }
            }
        }

        AllGroups = groups;
        AllKnownKeys = keys;
    }
}
