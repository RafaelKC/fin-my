using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FluentValidation;

namespace FinMy.Application.Profile;

/// <param name="CurrentPassword">Senha atual, para provar que é a própria pessoa.</param>
/// <param name="NewPassword">Nova senha.</param>
/// <param name="CurrentSessionId">Sessão (<c>sid</c>) que fez o pedido; é a única que continua aberta.</param>
public sealed record ChangePasswordCommand(string? CurrentPassword, string? NewPassword, Guid CurrentSessionId);

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(c => c.CurrentPassword).Must(p => !string.IsNullOrEmpty(p)).WithMessage("Informe sua senha atual.");

        RuleFor(c => c.NewPassword)
            .Must(p => !string.IsNullOrEmpty(p)).WithMessage("Informe a nova senha.")
            .Must(p => string.IsNullOrEmpty(p) || p.Length >= ProfileRules.PasswordMinLength)
            .WithMessage($"A senha precisa ter pelo menos {ProfileRules.PasswordMinLength} caracteres.")
            .Must(p => p is null || p.Length <= ProfileRules.PasswordMaxLength)
            .WithMessage($"A senha pode ter no máximo {ProfileRules.PasswordMaxLength} caracteres.");
    }
}

/// <summary>
/// Troca a senha informando a atual (US5, FR-013): renova o <c>security_stamp</c> e encerra as OUTRAS sessões
/// (<c>revoked_reason = password_changed</c>), pelo <see cref="IRefreshTokenStore"/> para invalidar o cache de sessões,
/// e revoga todas as autorizações OAuth (conectores, 008 FR-003).
/// </summary>
public sealed class ChangePasswordHandler(IProfileStore profiles, IPasswordHasher hasher, IRefreshTokenStore sessions, IOAuthAuthorizationRevoker oauth, ICurrentContext context, IClock clock)
{
    public const string CurrentPasswordIncorrectCode = "current_password_incorrect";

    private static readonly ChangePasswordValidator Validator = new();

    public async Task HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = context.UserId ?? throw MeErrors.Unauthorized();
        await Validator.ValidateAndThrowAsync(command, cancellationToken);

        var currentHash = await profiles.GetPasswordHashAsync(userId, cancellationToken) ?? throw MeErrors.Unauthorized();
        if (hasher.Verify(currentHash, command.CurrentPassword!) == PasswordCheck.Failed)
        {
            const string message = "A senha atual não confere.";
            throw new DomainException(new DomainError(
                CurrentPasswordIncorrectCode,
                message,
                DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["currentPassword"] = [message] }));
        }

        await profiles.ChangePasswordAsync(userId, hasher.Hash(command.NewPassword!), clock.UtcNow, cancellationToken);
        await sessions.RevokeUserSessionsAsync(userId, RefreshTokenRevocationReason.PasswordChanged, cancellationToken, command.CurrentSessionId);
        await oauth.RevokeAllAsync(userId, cancellationToken);
    }
}
