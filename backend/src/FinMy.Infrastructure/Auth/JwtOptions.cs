using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinMy.Infrastructure.Auth;

/// <summary>Configuração do access token (seção <c>Jwt</c>; a chave vem de <c>Jwt__SigningKey</c>).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public const int MinimumKeyLength = 32;

    /// <summary>Validade do access token (docs/arquitetura.md §7).</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "fin-my";

    public string Audience { get; set; } = "fin-my";

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>Parâmetros de validação do <c>JwtBearer</c>: assinatura, emissor, público e validade, sem tolerância de relógio.</summary>
    public TokenValidationParameters ToValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateKey(),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = TokenClaims.Name,
    };
}

/// <summary>Falha a subida da API quando a chave de assinatura falta ou é curta demais.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.SigningKey.Length < JwtOptions.MinimumKeyLength
            ? ValidateOptionsResult.Fail($"Jwt:SigningKey deve ter ao menos {JwtOptions.MinimumKeyLength} caracteres (defina Jwt__SigningKey).")
            : ValidateOptionsResult.Success;
    }
}
