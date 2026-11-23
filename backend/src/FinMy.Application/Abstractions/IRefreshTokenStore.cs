using FinMy.Domain.Identity;

namespace FinMy.Application.Abstractions;

/// <summary>Refresh token recém-emitido: <paramref name="Token"/> é o valor opaco (só existe aqui, o banco guarda o hash).</summary>
public sealed record IssuedRefreshToken(string Token, Guid SessionId, DateTimeOffset ExpiresAt);

public enum RefreshRotationStatus
{
    /// <summary>Token válido: foi trocado por um novo.</summary>
    Rotated,

    /// <summary>Token desconhecido.</summary>
    Invalid,

    /// <summary>Passou de 90 dias sem uso (<c>session_expired</c>).</summary>
    Expired,

    /// <summary>Sessão encerrada (logout, troca ou redefinição de senha) (<c>session_revoked</c>).</summary>
    Revoked,

    /// <summary>Token já trocado foi reapresentado: a sessão inteira foi revogada (<c>refresh_reused</c>).</summary>
    ReuseDetected,
}

/// <param name="Status">Resultado da troca.</param>
/// <param name="UserId">Dona da sessão, quando o token é conhecido.</param>
/// <param name="Token">Novo token, só quando <paramref name="Status"/> é <see cref="RefreshRotationStatus.Rotated"/>.</param>
public sealed record RefreshRotation(RefreshRotationStatus Status, Guid? UserId = null, IssuedRefreshToken? Token = null);

/// <summary>Guarda e troca refresh tokens opacos (256 bits, só o hash SHA-256 no banco).</summary>
public interface IRefreshTokenStore
{
    /// <summary>Emite um token; sem <paramref name="sessionId"/> abre uma sessão nova (login/cadastro).</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? sessionId, string? deviceLabel, CancellationToken cancellationToken);

    /// <summary>Troca o token apresentado por um novo (rotação) e prolonga a validade em 90 dias.</summary>
    Task<RefreshRotation> RotateAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Encerra a sessão a que o token pertence (logout). Não troca nem prolonga nada; token desconhecido ou
    /// sessão já encerrada não é erro (idempotente).
    /// </summary>
    Task RevokeSessionByTokenAsync(string token, RefreshTokenRevocationReason reason, CancellationToken cancellationToken);

    /// <summary>Encerra a sessão (todos os tokens ainda ativos da família).</summary>
    Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Encerra todas as sessões ativas da pessoa (ex.: redefinição de senha), invalidando o cache de cada uma.
    /// Com <paramref name="exceptSessionId"/>, essa sessão é mantida (ex.: troca de senha preserva a atual).
    /// </summary>
    Task RevokeUserSessionsAsync(Guid userId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken, Guid? exceptSessionId = null);
}
