using FinMy.Domain.Entries;

namespace FinMy.Application.Abstractions;

/// <summary>Consultas dos padrões de criação do Claude (008 FR-005): o que a pessoa usou antes com a mesma descrição.</summary>
public interface ICreationDefaultsStore
{
    /// <summary>Carteira ativa do lançamento mais recente com a descrição (já normalizada) e o tipo; <c>null</c> se não há ou foi arquivada.</summary>
    Task<Guid?> LatestWalletAsync(EntryKind kind, string normalizedDescription, CancellationToken cancellationToken);

    /// <summary>Cartão ativo da compra mais recente com a descrição (já normalizada); <c>null</c> se não há ou foi arquivado.</summary>
    Task<Guid?> LatestCreditCardAsync(string normalizedDescription, CancellationToken cancellationToken);

    /// <summary>Cartão ativo com mais compras desde <paramref name="since"/> (data da compra); <c>null</c> se nenhum teve compras.</summary>
    Task<Guid?> MostUsedCreditCardAsync(DateOnly since, CancellationToken cancellationToken);
}
