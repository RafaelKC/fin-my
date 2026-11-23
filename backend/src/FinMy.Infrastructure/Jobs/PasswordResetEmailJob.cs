using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMy.Infrastructure.Jobs;

/// <summary>
/// Job do Hangfire que cria o token e envia o e-mail de redefinição. Só o endereço vai nos argumentos do job:
/// o token é gerado dentro dele, então nunca fica gravado nas tabelas do Hangfire. Se o SMTP falhar o job lança
/// e o Hangfire tenta de novo.
/// </summary>
public sealed class PasswordResetEmailJob(PasswordResetMailer mailer)
{
    public Task RunAsync(string email, CancellationToken cancellationToken = default)
        => mailer.SendAsync(email, cancellationToken);
}

/// <summary>Despacha o envio como job do Hangfire: responde na hora, exista ou não a conta, e sobrevive a SMTP fora do ar.</summary>
public sealed class HangfirePasswordResetDispatcher(IBackgroundJobClient jobs) : IPasswordResetDispatcher
{
    public Task DispatchAsync(string email, CancellationToken cancellationToken)
    {
        jobs.Enqueue<PasswordResetEmailJob>(job => job.RunAsync(email, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sem Hangfire (<c>Hangfire:Enabled=false</c>, testes HTTP): envia na própria requisição, mas engole e registra
/// qualquer falha para que o pedido continue respondendo 202.
/// </summary>
public sealed partial class InlinePasswordResetDispatcher(PasswordResetMailer mailer, ILogger<InlinePasswordResetDispatcher> logger) : IPasswordResetDispatcher
{
    public async Task DispatchAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await mailer.SendAsync(email, cancellationToken);
        }
#pragma warning disable CA1031 // Qualquer falha de envio deve ficar invisível para quem pediu o link.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao enviar o e-mail de redefinição de senha")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}
