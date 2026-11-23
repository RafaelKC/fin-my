using FinMy.Domain.Common;

namespace FinMy.Application.Abstractions;

/// <summary>Dados que vão como claims no access token (<c>sub</c>, <c>tid</c>, <c>sid</c>, <c>name</c>, <c>channel</c>).</summary>
/// <param name="UserId">Pessoa autenticada (<c>sub</c>).</param>
/// <param name="TenantId">Espaço em que ela age (<c>tid</c>).</param>
/// <param name="SessionId">Sessão = <c>family_id</c> do refresh token (<c>sid</c>).</param>
/// <param name="Name">Nome da pessoa (<c>name</c>).</param>
/// <param name="Channel">Canal de origem (<c>channel</c>).</param>
public sealed record AccessTokenRequest(Guid UserId, Guid TenantId, Guid SessionId, string Name, Channel Channel);

/// <summary>Access token emitido e o instante em que expira.</summary>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Emite o access token JWT de 15 minutos.</summary>
public interface ITokenService
{
    IssuedAccessToken IssueAccessToken(AccessTokenRequest request);
}
