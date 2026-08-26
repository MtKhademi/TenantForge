using System.Security.Cryptography;
using System.Text;

namespace TenantForge.Modules.Iam.Features.Login;

public sealed class DevelopmentCredentialChecker(DevelopmentLoginOptions options) : ICredentialChecker
{
    public bool Check(string email, string password)
    {
        var expectedEmail = options.Email;
        var expectedPassword = options.Password;
        if (string.IsNullOrWhiteSpace(expectedEmail) || string.IsNullOrEmpty(expectedPassword))
        {
            return false;
        }

        var emailMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(email),
            Encoding.UTF8.GetBytes(expectedEmail.Trim()));
        var passwordMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password),
            Encoding.UTF8.GetBytes(expectedPassword));

        return emailMatches && passwordMatches;
    }
}
