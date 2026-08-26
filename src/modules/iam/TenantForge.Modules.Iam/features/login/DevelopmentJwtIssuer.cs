using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace TenantForge.Modules.Iam.Features.Login;

public sealed class DevelopmentJwtIssuer(DevelopmentLoginOptions options)
{
    private readonly SymmetricSecurityKey _signingKey = new(
        Encoding.UTF8.GetBytes(options.SigningKey
            ?? throw new InvalidOperationException("DevelopmentLogin:SigningKey is required to issue development tokens.")));

    public (string AccessToken, DateTimeOffset ExpiresAtUtc) IssueForAdmin(string email)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(DevelopmentAdminIdentity.TokenLifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, DevelopmentAdminIdentity.Id),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Name, DevelopmentAdminIdentity.DisplayName),
                new Claim("isPlatformAdmin", "true")
            ]),
            Audience = DevelopmentAdminIdentity.Audience,
            Issuer = DevelopmentAdminIdentity.Issuer,
            Expires = expiresAtUtc.UtcDateTime,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(handler.CreateToken(descriptor));

        return (accessToken, expiresAtUtc);
    }
}
