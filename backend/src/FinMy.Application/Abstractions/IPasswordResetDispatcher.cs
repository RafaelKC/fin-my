namespace FinMy.Application.Abstractions;

/// <summary>
/// Despacho do envio do e-mail de redefinição. Enfileirar não depende de existir conta nem do SMTP; quem consome chama
/// <c>PasswordResetMailer.SendAsync</c>.
/// </summary>
public interface IPasswordResetDispatcher
{
    Task DispatchAsync(string email, CancellationToken cancellationToken);
}
