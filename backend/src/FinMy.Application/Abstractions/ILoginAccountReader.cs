using FinMy.Domain.Identity;

namespace FinMy.Application.Abstractions;

/// <summary>Conta como o login a enxerga: o usuário, o Espaço e a participação dele.</summary>
public sealed record LoginAccount(User User, Tenant Tenant, TenantMember Member);

/// <summary>Leitura da conta para o login (tabelas globais; não depende de contexto autenticado).</summary>
public interface ILoginAccountReader
{
    /// <summary>Conta pelo <c>email_normalized</c>, ou <c>null</c> se não existe (ou não tem Espaço).</summary>
    Task<LoginAccount?> FindAsync(string emailNormalized, CancellationToken cancellationToken);
}
