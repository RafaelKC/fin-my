using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Auth;

/// <param name="Email">E-mail informado na tela "Esqueci minha senha".</param>
public sealed record RequestPasswordResetCommand(string? Email);

/// <summary>
/// Pede o link de redefinição (US4, FR-011, SC-004). Só valida o formato e enfileira o envio: a resposta é a mesma
/// e leva o mesmo tempo exista ou não a conta, e uma falha de SMTP não aparece para quem pediu.
/// </summary>
public sealed class RequestPasswordResetHandler(IPasswordResetDispatcher queue)
{
    public const int EmailMaxLength = 254;

    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var address = command.Email?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > EmailMaxLength)
        {
            throw new DomainException(new DomainError(
                "validation_failed",
                "Informe um e-mail válido.",
                DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["email"] = ["Informe um e-mail válido."] }));
        }

        await queue.DispatchAsync(address, cancellationToken);
    }
}
