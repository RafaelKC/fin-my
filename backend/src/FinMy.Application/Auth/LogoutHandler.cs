using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <summary>
/// Encerra a sessão deste aparelho a partir do refresh token do cookie. Idempotente: sem cookie, com token
/// desconhecido ou com a sessão já encerrada, não há erro.
/// </summary>
public sealed class LogoutHandler(IRefreshTokenStore refreshTokens)
{
    public async Task HandleAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        await refreshTokens.RevokeSessionByTokenAsync(refreshToken, RefreshTokenRevocationReason.Logout, cancellationToken);
    }
}
