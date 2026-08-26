namespace TenantForge.Modules.Iam.Features.Login;

public interface ICredentialChecker
{
    bool Check(string email, string password);
}
