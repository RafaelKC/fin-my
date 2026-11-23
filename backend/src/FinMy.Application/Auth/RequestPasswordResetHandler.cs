using System.Net;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <param name="Email">E-mail informado na tela "Esqueci minha senha".</param>
public sealed record RequestPasswordResetCommand(string? Email);

/// <summary>
/// Pede o link de redefinição (US4, FR-011). Termina igual exista ou não a conta, para não revelar quem tem cadastro:
/// só quando há conta é gravado o token (1 h, uso único) e enviado o e-mail.
/// </summary>
public sealed class RequestPasswordResetHandler(IPasswordResetStore store, IEmailSender email, IClock clock, AppUrl appUrl)
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

        var token = PasswordResetTokens.Generate();
        var now = clock.UtcNow;
        var recipient = await store.CreateAsync(
            User.NormalizeEmail(address),
            PasswordResetTokens.Hash(token),
            now,
            now.Add(PasswordResetTokens.Lifetime),
            cancellationToken);

        if (recipient is null)
        {
            return;
        }

        var link = appUrl.Combine($"/redefinir-senha?token={Uri.EscapeDataString(token)}");
        await email.SendAsync(BuildMessage(recipient, link), cancellationToken);
    }

    private static EmailMessage BuildMessage(PasswordResetRecipient recipient, string link)
    {
        var text = $"""
            Olá, {recipient.Name}!

            Recebemos um pedido para redefinir a senha da sua conta no Fin-My.
            Para escolher uma nova senha, abra o link abaixo. Ele vale por 1 hora e só pode ser usado uma vez:

            {link}

            Se você não fez esse pedido, ignore este e-mail: sua senha continua a mesma.
            """;

        var html = $"""
            <p>Olá, {WebUtility.HtmlEncode(recipient.Name)}!</p>
            <p>Recebemos um pedido para redefinir a senha da sua conta no Fin-My.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Escolher uma nova senha</a></p>
            <p>O link vale por 1 hora e só pode ser usado uma vez.</p>
            <p>Se você não fez esse pedido, ignore este e-mail: sua senha continua a mesma.</p>
            """;

        return new EmailMessage(recipient.Email, "Redefinição de senha do Fin-My", text, html);
    }
}
