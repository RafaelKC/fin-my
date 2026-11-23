namespace FinMy.Application.Abstractions;

/// <summary>Quem recebe o e-mail de redefinição.</summary>
public sealed record PasswordResetRecipient(Guid UserId, string Name, string Email);

/// <summary>Persistência da recuperação de senha (<c>password_reset_tokens</c> e a troca da senha do usuário).</summary>
public interface IPasswordResetStore
{
    /// <summary>
    /// Guarda o hash de um novo token para a conta com o e-mail normalizado informado. Devolve a pessoa,
    /// ou <c>null</c> quando não há conta (nada é gravado). Os tokens anteriores continuam válidos.
    /// </summary>
    Task<PasswordResetRecipient?> CreateAsync(string emailNormalized, byte[] tokenHash, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>
    /// Consome o token (uso único) e troca a senha e o <c>security_stamp</c> do usuário, tudo numa transação.
    /// Devolve o usuário, ou <c>null</c> se o token não existe, expirou ou já foi usado.
    /// </summary>
    Task<Guid?> RedeemAsync(byte[] tokenHash, string newPasswordHash, DateTimeOffset now, CancellationToken cancellationToken);
}
