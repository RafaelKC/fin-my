using FinMy.Application.Abstractions;

namespace FinMy.Application.Tests.Email;

/// <summary>Guarda as mensagens enviadas para os testes inspecionarem (research R6).</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent => _sent;

    /// <summary>Quando definido, o envio falha com esta exceção (SMTP fora do ar).</summary>
    public Exception? Failure { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        _sent.Add(message);
        return Task.CompletedTask;
    }
}
