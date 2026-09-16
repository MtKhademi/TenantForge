namespace TenantForge.Modules.Iam.Contract.Requests;

public sealed record CreateUserRequest(string? Email, string? DisplayName, string? Password);
