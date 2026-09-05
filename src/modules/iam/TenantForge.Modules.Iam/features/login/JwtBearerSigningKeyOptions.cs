using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Assigns the signing key to the JwtBearer validation parameters at runtime.
/// The key comes from the lazily bound <see cref="AuthOptions"/> so late
/// configuration sources (including WebApplicationFactory test configuration)
/// are honored; the static validation rules live in IAMConfig.
/// </summary>
internal sealed class JwtBearerSigningKeyOptions(AuthOptions authOptions)
    : IPostConfigureOptions<JwtBearerOptions>
{
    // Generated once per process. When no signing key is configured this random
    // key is used: no client can know it, every signature check fails, and
    // protected endpoints fail closed with 401 instead of the handler throwing
    // a 500.
    private static readonly SymmetricSecurityKey NeverMatchingKey =
        new(RandomNumberGenerator.GetBytes(32));

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        options.TokenValidationParameters.IssuerSigningKey =
            string.IsNullOrWhiteSpace(authOptions.SigningKey)
                ? NeverMatchingKey
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey));
    }
}
