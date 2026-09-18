namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// One named group of permissions a module owns. Mirrors
/// TenantForge.Modules.Iam.Contract.Responses.PermissionGroupResponse
/// field-for-field, for the same reason as PermissionDescriptor.
/// </summary>
public sealed record PermissionGroup(string Id, string Label, string Description, IReadOnlyList<PermissionDescriptor> Permissions);
