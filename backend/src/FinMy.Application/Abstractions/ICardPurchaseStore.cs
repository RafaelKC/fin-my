using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Compra ou parcela como o cliente a vê (contrato <c>CardPurchase</c>); <see cref="Version"/> é o <c>xmin</c> (research R11).</summary>
/// <param name="SplitCount">Partes que o grupo de divisão tem hoje (004 FR-014; parcelamento conta como uma); 0 se não foi dividida.</param>
/// <param name="DueMonth">Mês de vencimento da fatura onde a compra está.</param>
public sealed record CardPurchaseView(
    Guid Id,
    Guid CreditCardId,
    YearMonth DueMonth,
    string Description,
    string DisplayDescription,
    long AmountCents,
    PurchaseNature Nature,
    DateOnly Date,
    PurchaseOrigin Origin,
    bool Reconciled,
    bool ManuallyMoved,
    string? Notes,
    Guid? InstallmentPlanId,
    int? InstallmentNumber,
    int? InstallmentTotal,
    DateTimeOffset CreatedAt,
    long Version,
    IReadOnlyList<Guid> TagIds,
    Guid? SettlementTagId,
    Guid? SplitGroupId,
    int SplitCount);

/// <summary>Total (débitos − créditos) e pago de uma fatura (003 FR-005).</summary>
public readonly record struct InvoiceTotals(long TotalCents, long PaidCents);

/// <summary>Persistência de compras e parcelamentos do Espaço do contexto (003 US2–US4, US7).</summary>
public interface ICardPurchaseStore
{
    /// <summary>Compra rastreada para alteração, ou <c>null</c> se não existe no Espaço.</summary>
    Task<CardPurchase?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<InstallmentPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>Parcelas do plano, rastreadas, por número.</summary>
    Task<IReadOnlyList<CardPurchase>> ListPlanAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>As duas compras do par de "saldo levado" (inclui a própria), rastreadas.</summary>
    Task<IReadOnlyList<CardPurchase>> ListCarryOverPairAsync(Guid pairId, CancellationToken cancellationToken);

    /// <summary>Faturas por id (mês e datas), sem rastreamento.</summary>
    Task<IReadOnlyDictionary<Guid, Invoice>> GetInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, InvoiceTotals>> GetInvoiceTotalsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    /// <summary>Leitura atual (com versão e mês da fatura) das compras, por número de parcela e criação.</summary>
    Task<IReadOnlyList<CardPurchaseView>> GetViewsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Todas as tags da compra (a de acerto primeiro), lidas do banco.</summary>
    Task<IReadOnlyList<Guid>> GetTagIdsAsync(CardPurchase purchase, CancellationToken cancellationToken);

    /// <summary>Liga uma compra nova às <paramref name="commonTagIds"/> (grava em <see cref="SaveAsync"/>).</summary>
    void AddTags(CardPurchase purchase, IReadOnlyCollection<Guid> commonTagIds);

    /// <summary>Faz as ligações da compra serem exatamente as <paramref name="commonTagIds"/> (grava em <see cref="SaveAsync"/>).</summary>
    Task ReplaceTagsAsync(CardPurchase purchase, IReadOnlyCollection<Guid> commonTagIds, CancellationToken cancellationToken);

    long VersionOf(CardPurchase purchase);

    /// <summary>Exige que a gravação encontre a compra ainda nesta versão (senão <c>concurrency_conflict</c>).</summary>
    void ExpectVersion(CardPurchase purchase, long version);

    void Add(CardPurchase purchase);

    void Add(InstallmentPlan plan);

    /// <summary>Exclusão lógica no lote corrente do contexto.</summary>
    void Remove(CardPurchase purchase);

    void Remove(InstallmentPlan plan);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<ICardTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
