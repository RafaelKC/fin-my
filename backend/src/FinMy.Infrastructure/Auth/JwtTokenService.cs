using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FinMy.Infrastructure.Auth;

/// <summary>Emite o access token JWT (HS256, 15 min) com <c>sub</c>, <c>tid</c>, <c>sid</c>, <c>name</c> e <c>channel</c>.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedAccessToken IssueAccessToken(AccessTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jwt = options.Value;
        var now = clock.UtcNow;
        var expiresAt = now.Add(JwtOptions.AccessTokenLifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [TokenClaims.Subject] = request.UserId.ToString(),
                [TokenClaims.Tenant] = request.TenantId.ToString(),
                [TokenClaims.Session] = request.SessionId.ToString(),
                [TokenClaims.Name] = request.Name,
                [TokenClaims.Channel] = request.Channel.ToCode(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
