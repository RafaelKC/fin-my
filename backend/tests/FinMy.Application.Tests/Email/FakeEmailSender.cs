using FinMy.Application.Abstractions;

namespace FinMy.Application.Tests.Email;

/// <summary>Guarda as mensagens enviadas para os testes inspecionarem (research R6).</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Add(message);
        return Task.CompletedTask;
    }
}
