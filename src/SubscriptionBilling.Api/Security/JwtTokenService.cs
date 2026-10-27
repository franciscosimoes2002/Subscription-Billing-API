using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SubscriptionBilling.Api.Options;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Api.Security;

/// <summary>Issues short-lived HS256 access tokens. The same options feed token validation, so the two cannot drift apart.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public const string RoleClaim = "role";

    private readonly JsonWebTokenHandler _handler = new();
    private readonly JwtOptions _options = options.Value;

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public AccessToken Issue(User user, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        var expires = nowUtc.AddMinutes(_options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(RoleClaim, user.Role.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(CreateKey(_options.SigningKey), SecurityAlgorithms.HmacSha256),
        };
        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
