namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// One permission key a module owns, with its display label, description
/// and kind ("read"/"write"). Mirrors
/// TenantForge.Modules.Iam.Contract.Responses.PermissionResponse
/// field-for-field — that type stays IAM's own HTTP wire shape; this one
/// is the cross-module in-memory shape every contributor returns.
/// </summary>
public sealed record PermissionDescriptor(string Key, string Label, string Description, string Kind);
