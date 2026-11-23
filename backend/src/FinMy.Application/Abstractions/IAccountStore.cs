using FinMy.Domain.Identity;

namespace FinMy.Application.Abstractions;

public enum AccountCreation
{
    Created,

    /// <summary>Já existe um usuário com o mesmo <c>email_normalized</c> (índice único; research R7).</summary>
    EmailAlreadyRegistered,
}

/// <summary>Grava uma conta nova: Usuário, Espaço e Participação na mesma transação.</summary>
public interface IAccountStore
{
    /// <summary>
    /// Grava as três entidades de uma vez. Não há usuário no contexto ainda: a implementação coloca o novo
    /// usuário e Espaço no <see cref="ICurrentContext"/> antes de salvar, para a auditoria ter autor.
    /// A violação de unicidade do e-mail vira <see cref="AccountCreation.EmailAlreadyRegistered"/>.
    /// </summary>
    Task<AccountCreation> CreateAsync(User user, Tenant tenant, TenantMember member, CancellationToken cancellationToken);
}
