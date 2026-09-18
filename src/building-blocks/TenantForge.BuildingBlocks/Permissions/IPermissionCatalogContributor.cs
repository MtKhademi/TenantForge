namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// Implemented once per module that owns permission keys. Registered as
/// `IPermissionCatalogContributor` in the module's own `RegisterServices`
/// (see IAMConfig.cs and, in B035, ShopConfig.cs) so the API host can
/// discover every contributor without any module referencing another.
/// </summary>
public interface IPermissionCatalogContributor
{
    IReadOnlyList<PermissionGroup> GetPermissionGroups();
}
