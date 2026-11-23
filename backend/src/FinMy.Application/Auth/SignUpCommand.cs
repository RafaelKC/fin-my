using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <summary>Dados do cadastro, como chegaram (podem estar nulos ou com espaços; a validação cuida).</summary>
/// <param name="DeviceLabel">Rótulo do aparelho (ex.: User-Agent) guardado na sessão; opcional.</param>
public sealed record SignUpCommand(string? Name, string? Email, string? Password, string? DeviceLabel = null);

/// <summary>Usuário criado, como o cliente o vê.</summary>
public sealed record UserSummary(Guid Id, string Name, string Email);

/// <summary>Espaço criado e o papel da pessoa nele.</summary>
public sealed record TenantSummary(Guid Id, string Name, TenantMemberRole Role);

/// <summary>Sessão aberta pelo cadastro: o access token vai no corpo; <paramref name="RefreshToken"/> só no cookie.</summary>
public sealed record SignUpResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    UserSummary User,
    TenantSummary Tenant);
