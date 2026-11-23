namespace FinMy.Application.Abstractions;

/// <summary>Mensagem de e-mail em texto simples com HTML opcional (research R6).</summary>
/// <param name="To">Endereço do destinatário.</param>
/// <param name="Subject">Assunto.</param>
/// <param name="TextBody">Corpo em texto simples (sempre presente).</param>
/// <param name="HtmlBody">Corpo HTML mínimo, opcional.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>Envio de e-mails transacionais. Implementação real via SMTP; testes usam um fake.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
