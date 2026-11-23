namespace FinMy.Infrastructure.Email;

/// <summary>Configuração SMTP (seção <c>Email</c>). Em desenvolvimento aponta para o Mailpit, sem TLS e sem credenciais.</summary>
public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary>Quando verdadeiro, exige STARTTLS (ou TLS direto na porta 465); falso para o Mailpit local.</summary>
    public bool UseTls { get; set; }

    public string? User { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "nao-responda@fin-my.local";

    public string FromName { get; set; } = "Fin-My";
}
