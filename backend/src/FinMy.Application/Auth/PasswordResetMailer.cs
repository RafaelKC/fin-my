using System.Net;
using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <summary>
/// Cria o token de redefinição (1 h, uso único) e envia o e-mail quando existe conta com o endereço.
/// Roda em segundo plano (<see cref="IPasswordResetDispatcher"/>): nem o tempo nem uma falha de SMTP aparecem na resposta do pedido.
/// </summary>
public sealed class PasswordResetMailer(IPasswordResetStore store, IEmailSender email, IClock clock, AppUrl appUrl)
{
    public async Task SendAsync(string address, CancellationToken cancellationToken)
    {
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
