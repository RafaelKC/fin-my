using FinMy.Domain.Wallets;

namespace FinMy.Application.Abstractions;

/// <summary>Carteira com os agregados da lista (research R5): saldo atual e se já tem lançamentos.</summary>
/// <param name="Version">Token de concorrência (<c>xmin</c>) como número (research R11).</param>
public sealed record WalletView(
    Guid Id,
    string Name,
    WalletType Type,
    string Color,
    long InitialBalanceCents,
    DateOnly InitialBalanceDate,
    bool IsPrimary,
    bool Archived,
    long CurrentBalanceCents,
    bool HasEntries,
    long Version,
    string? Bank = null);

/// <summary>Quais carteiras a lista devolve (<c>archived</c>).</summary>
public enum WalletArchivedFilter
{
    ActiveOnly,
    ArchivedOnly,
    All,
}

/// <summary>Transação curta usada na troca de principal (research R6): dois <c>SaveChanges</c> tudo ou nada.</summary>
public interface IWalletTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Persistência das carteiras do Espaço do contexto (002 US1).</summary>
public interface IWalletStore
{
    /// <summary>Carteira rastreada para alteração (inclusive arquivada), ou <c>null</c> se não existe no Espaço.</summary>
    Task<Wallet?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A carteira principal ativa, rastreada, ou <c>null</c> se não há.</summary>
    Task<Wallet?> FindPrimaryAsync(CancellationToken cancellationToken);

    /// <summary>Existe alguma carteira ativa, excluída a de id <paramref name="exceptId"/>?</summary>
    Task<bool> AnyActiveAsync(Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Alguma linha (mesmo excluída e ainda não expurgada) de lançamentos ou transferências usa a carteira?</summary>
    Task<bool> HasEntriesAsync(Guid id, CancellationToken cancellationToken);

    Task<WalletView?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WalletView>> ListAsync(WalletArchivedFilter filter, CancellationToken cancellationToken);

    /// <summary>Token de concorrência atual da carteira rastreada.</summary>
    long VersionOf(Wallet wallet);

    /// <summary>Exige que a gravação encontre a carteira ainda nesta versão (senão <c>concurrency_conflict</c>).</summary>
    void ExpectVersion(Wallet wallet, long version);

    void Add(Wallet wallet);

    void Remove(Wallet wallet);

    /// <summary>Grava; nome repetido vira <c>wallet.name_taken</c> (409).</summary>
    Task SaveAsync(CancellationToken cancellationToken);

    Task<IWalletTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
