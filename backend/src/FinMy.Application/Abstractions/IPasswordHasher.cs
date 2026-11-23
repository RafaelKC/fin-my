namespace FinMy.Application.Abstractions;

/// <summary>Resultado da conferência de uma senha contra o hash guardado.</summary>
public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>A senha confere, mas o hash usa parâmetros antigos: vale gravar um novo hash.</summary>
    SuccessRehashNeeded,
}

/// <summary>Hash e conferência de senhas (docs/arquitetura.md §7).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string password);
}
