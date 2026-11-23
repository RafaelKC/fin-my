using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <param name="Token">Token recebido por e-mail.</param>
/// <param name="NewPassword">Nova senha escolhida.</param>
public sealed record ConfirmPasswordResetCommand(string? Token, string? NewPassword);

/// <summary>
/// Conclui a recuperação (US4, FR-011/FR-012): troca a senha, renova o <c>security_stamp</c>, marca o token como usado
/// e encerra todas as sessões da pessoa (<c>revoked_reason = password_reset</c>).
/// </summary>
public sealed class ConfirmPasswordResetHandler(IPasswordResetStore store, IPasswordHasher hasher, IRefreshTokenStore sessions, IClock clock)
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public const string InvalidTokenCode = "reset_token_invalid";

    public async Task HandleAsync(ConfirmPasswordResetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var password = command.NewPassword;
        if (password is null || password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
        {
            const string message = "A senha deve ter de 8 a 128 caracteres.";
            throw new DomainException(new DomainError(
                "validation_failed",
                message,
                DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["newPassword"] = [message] }));
        }

        var userId = string.IsNullOrWhiteSpace(command.Token)
            ? null
            : await store.RedeemAsync(PasswordResetTokens.Hash(command.Token), hasher.Hash(password), clock.UtcNow, cancellationToken);

        if (userId is null)
        {
            throw new DomainException(new DomainError(
                InvalidTokenCode,
                "Este link expirou ou já foi usado. Peça um novo link.",
                DomainErrorKind.Gone));
        }

        await sessions.RevokeUserSessionsAsync(userId.Value, RefreshTokenRevocationReason.PasswordReset, cancellationToken);
    }
}
