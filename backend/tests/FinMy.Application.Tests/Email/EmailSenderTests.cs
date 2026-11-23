using System.Net;
using System.Net.Sockets;
using System.Text;
using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace FinMy.Application.Tests.Email;

public class EmailSenderTests
{
    private static SmtpEmailSender Smtp(int port = 1025) =>
        new(Options.Create(new SmtpEmailOptions { Port = port, FromAddress = "app@fin-my.test", FromName = "Fin-My" }));

    [Fact]
    public async Task FakeEmailSender_guarda_as_mensagens_enviadas()
    {
        var fake = new FakeEmailSender();

        await fake.SendAsync(new EmailMessage("a@b.com", "Assunto", "texto"));

        fake.Sent.ShouldHaveSingleItem().Subject.ShouldBe("Assunto");
        fake.Sent[0].To.ShouldBe("a@b.com");
    }

    [Fact]
    public void BuildMimeMessage_monta_remetente_destinatario_e_corpos()
    {
        var mime = Smtp().BuildMimeMessage(new EmailMessage("maria@exemplo.com", "Redefinir senha", "texto", "<p>html</p>"));

        mime.From.Mailboxes.Single().Address.ShouldBe("app@fin-my.test");
        mime.From.Mailboxes.Single().Name.ShouldBe("Fin-My");
        mime.To.Mailboxes.Single().Address.ShouldBe("maria@exemplo.com");
        mime.Subject.ShouldBe("Redefinir senha");
        mime.TextBody.ShouldBe("texto");
        mime.HtmlBody.ShouldBe("<p>html</p>");
    }

    [Fact]
    public void BuildMimeMessage_sem_html_envia_so_texto()
    {
        var mime = Smtp().BuildMimeMessage(new EmailMessage("maria@exemplo.com", "Oi", "texto"));

        mime.TextBody.ShouldBe("texto");
        mime.HtmlBody.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_entrega_a_mensagem_a_um_servidor_SMTP()
    {
        using var server = new MiniSmtpServer();
        var sender = Smtp(server.Port);

        await sender.SendAsync(new EmailMessage("maria@exemplo.com", "Redefinir senha", "link: http://app/redefinir-senha?token=abc"));

        var data = await server.Received.WaitAsync(TimeSpan.FromSeconds(10));
        data.ShouldContain("RCPT TO:<maria@exemplo.com>");
        data.ShouldContain("Subject: Redefinir senha");
        data.ShouldContain("redefinir-senha?token=abc");
    }

    /// <summary>Servidor SMTP mínimo (sem TLS/AUTH) que aceita uma mensagem e guarda a conversa.</summary>
    private sealed class MiniSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource<string> _received = new();

        public MiniSmtpServer()
        {
            _listener.Start();
            _ = Task.Run(RunAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task<string> Received => _received.Task;

        public void Dispose() => _listener.Stop();

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                var log = new StringBuilder();

                await writer.WriteLineAsync("220 mini ESMTP");
                var inData = false;
                while (await reader.ReadLineAsync() is { } line)
                {
                    log.AppendLine(line);
                    if (inData)
                    {
                        if (line == ".")
                        {
                            inData = false;
                            await writer.WriteLineAsync("250 OK");
                        }

                        continue;
                    }

                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 mini");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        inData = true;
                        await writer.WriteLineAsync("354 go");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 bye");
                        break;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }

                _received.TrySetResult(log.ToString());
            }
            catch (Exception ex)
            {
                _received.TrySetException(ex);
            }
        }
    }
}
