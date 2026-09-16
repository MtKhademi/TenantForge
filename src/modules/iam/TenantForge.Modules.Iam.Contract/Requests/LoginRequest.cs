namespace TenantForge.Modules.Iam.Contract.Requests;

public sealed record LoginRequest(string? Email, string? Password);
