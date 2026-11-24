using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Cartão como o cliente o vê: <see cref="Version"/> é o token de concorrência (<c>xmin</c>, research R11).</summary>
/// <param name="HasPurchases">Alguma compra ou pagamento (mesmo excluído e ainda não expurgado) referencia o cartão (RT-11).</param>
public sealed record CreditCardView(
    Guid Id,
    string Name,
    int ClosingDay,
    int DueDay,
    Guid PaymentWalletId,
    string Color,
    long? LimitCents,
    bool Archived,
    bool HasPurchases,
    long Version);

/// <summary>Resumo de uma fatura (contrato <c>InvoiceSummary</c>); <see cref="Id"/> é <c>null</c> quando a linha ainda não existe (research R2).</summary>
public sealed record InvoiceSummaryView(
    Guid CreditCardId,
    YearMonth DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    Guid? Id,
    InvoiceStatus Status,
    long TotalCents,
    long PaidCents,
    long RemainingCents,
    long? EstimateCents = null,
    bool EstimateActive = false);

/// <summary>Cartão com fatura atual, próxima, faturas passadas em aberto e limite disponível (research R10, FR-025/FR-026).</summary>
public sealed record CreditCardOverviewView(
    CreditCardView Card,
    InvoiceSummaryView CurrentInvoice,
    InvoiceSummaryView NextInvoice,
    IReadOnlyList<InvoiceSummaryView> OpenPastInvoices,
    long? AvailableLimitCents);

/// <summary>Quais cartões a lista devolve (<c>archived</c>).</summary>
public enum CreditCardArchivedFilter
{
    ActiveOnly,
    ArchivedOnly,
    All,
}

/// <summary>Transação curta usada na mudança de dias do cartão (research R3): tudo ou nada.</summary>
public interface ICardTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Persistência dos cartões do Espaço do contexto (003 US1, US6).</summary>
public interface ICreditCardStore
{
    /// <summary>Cartão rastreado para alteração (inclusive arquivado), ou <c>null</c> se não existe no Espaço.</summary>
    Task<CreditCard?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<CreditCardView?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<CreditCardOverviewView?> GetOverviewAsync(Guid id, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditCardOverviewView>> ListOverviewsAsync(CreditCardArchivedFilter filter, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Alguma compra ou pagamento (mesmo excluído e ainda não expurgado) usa o cartão?</summary>
    Task<bool> HasPurchasesOrPaymentsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A fatura do cartão que vence em <paramref name="dueMonth"/>, sem criá-la; <c>null</c> se a linha não existe.</summary>
    Task<Invoice?> FindInvoiceAsync(Guid creditCardId, YearMonth dueMonth, CancellationToken cancellationToken);

    /// <summary>Todas as faturas do cartão, rastreadas (mudança de dias, research R3).</summary>
    Task<IReadOnlyList<Invoice>> ListInvoicesAsync(Guid creditCardId, CancellationToken cancellationToken);

    /// <summary>Compras das faturas, rastreadas (mudança de dias, research R3).</summary>
    Task<IReadOnlyList<CardPurchase>> ListPurchasesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, InstallmentPlan>> GetPlansAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);

    long VersionOf(CreditCard card);

    /// <summary>Exige que a gravação encontre o cartão ainda nesta versão (senão <c>concurrency_conflict</c>).</summary>
    void ExpectVersion(CreditCard card, long version);

    void Add(CreditCard card);

    void Remove(CreditCard card);

    /// <summary>Grava; nome repetido vira <c>credit_card.name_taken</c> (409).</summary>
    Task SaveAsync(CancellationToken cancellationToken);

    Task<ICardTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
