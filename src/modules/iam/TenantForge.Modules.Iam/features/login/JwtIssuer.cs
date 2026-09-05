using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Issues HS256 tokens for an already-authenticated account. The signing key
/// comes from <see cref="AuthOptions.SigningKey"/> (bound lazily from
/// <c>IAM:Auth:SigningKey</c> so late configuration sources, including test
/// hosts, are honored). Identity fields are taken from the verified account,
/// never from the request. When no signing key is configured the issuer is
/// unable to mint tokens (<see cref="CanIssue"/>) so the login endpoint fails
/// closed instead of throwing a 500.
/// </summary>
public sealed class JwtIssuer(AuthOptions options)
{
    private readonly string? _signingKey = options.SigningKey;

    public bool CanIssue => !string.IsNullOrWhiteSpace(_signingKey);

    public (string AccessToken, DateTimeOffset ExpiresAtUtc) Issue(AuthenticatedAccount account)
    {
        var key = _signingKey
            ?? throw new InvalidOperationException("IAM:Auth:SigningKey is required to issue tokens.");

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(JwtConstants.TokenLifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, account.Id),
                new Claim(JwtRegisteredClaimNames.Email, account.Email),
                new Claim(JwtRegisteredClaimNames.Name, account.DisplayName),
                new Claim("isPlatformAdmin", account.IsPlatformAdmin ? "true" : "false")
            ]),
            Audience = JwtConstants.Audience,
            Issuer = JwtConstants.Issuer,
            Expires = expiresAtUtc.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(handler.CreateToken(descriptor));

        return (accessToken, expiresAtUtc);
    }
}
