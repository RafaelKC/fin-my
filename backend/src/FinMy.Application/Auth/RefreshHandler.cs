using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <summary>
/// Renova a sessão a partir do refresh token do cookie: o token apresentado é trocado por um novo (rotação) e a
/// validade de 90 dias recomeça (deslizante). Token já trocado que reaparece derruba a sessão inteira
/// (<c>reuse_detected</c>) e responde <c>refresh_reused</c>.
/// </summary>
public sealed class RefreshHandler(
    IRefreshTokenStore refreshTokens,
    ILoginAccountReader accounts,
    ITokenService tokens)
{
    public const string SessionExpiredCode = "session_expired";
    public const string SessionRevokedCode = "session_revoked";
    public const string RefreshReusedCode = "refresh_reused";

    public static readonly DomainError SessionExpired = new(
        SessionExpiredCode,
        "Sua sessão expirou. Entre novamente.",
        DomainErrorKind.Unauthorized);

    public static readonly DomainError SessionRevoked = new(
        SessionRevokedCode,
        "Esta sessão foi encerrada. Entre novamente.",
        DomainErrorKind.Unauthorized);

    public static readonly DomainError RefreshReused = new(
        RefreshReusedCode,
        "Por segurança, encerramos esta sessão. Entre novamente.",
        DomainErrorKind.Unauthorized);

    public async Task<SignUpResult> HandleAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        // Sem cookie ou com token desconhecido não há sessão a renovar: é o mesmo que ter expirado.
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new DomainException(SessionExpired);
        }

        var rotation = await refreshTokens.RotateAsync(refreshToken, cancellationToken);

        switch (rotation.Status)
        {
            case RefreshRotationStatus.Rotated:
                break;
            case RefreshRotationStatus.Expired:
            case RefreshRotationStatus.Invalid:
                throw new DomainException(SessionExpired);
            case RefreshRotationStatus.Revoked:
                throw new DomainException(SessionRevoked);
            case RefreshRotationStatus.ReuseDetected:
                throw new DomainException(RefreshReused);
            default:
                throw new InvalidOperationException($"Resultado de rotação inesperado: {rotation.Status}.");
        }

        var issued = rotation.Token!;
        var account = await accounts.FindByUserIdAsync(rotation.UserId!.Value, cancellationToken);
        if (account is null)
        {
            // Pessoa sem conta/Espaço: não há o que servir; encerra a sessão que acabou de ser renovada.
            await refreshTokens.RevokeSessionAsync(issued.SessionId, RefreshTokenRevocationReason.Logout, cancellationToken);
            throw new DomainException(SessionRevoked);
        }

        var user = account.User;
        var access = tokens.IssueAccessToken(new AccessTokenRequest(user.Id, account.Tenant.Id, issued.SessionId, user.Name, Channel.App));

        return new SignUpResult(
            access.Token,
            access.ExpiresAt,
            issued.Token,
            new UserSummary(user.Id, user.Name, user.Email),
            new TenantSummary(account.Tenant.Id, account.Tenant.Name, account.Member.Role));
    }
}
