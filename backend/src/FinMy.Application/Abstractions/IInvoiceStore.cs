using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Pagamento de fatura como o cliente o vê (contrato <c>InvoicePayment</c>); <see cref="Version"/> é o <c>xmin</c>.</summary>
public sealed record InvoicePaymentView(
    Guid Id,
    Guid CreditCardId,
    YearMonth DueMonth,
    Guid WalletId,
    long AmountCents,
    DateOnly Date,
    long Version);

/// <summary>Fatura completa (contrato <c>Invoice</c>): resumo, conferido, compras, pagamentos e meses vizinhos (US5, US6.2, FR-027).</summary>
public sealed record InvoiceDetailView(
    InvoiceSummaryView Summary,
    long ReconciledCents,
    bool CanCarryOver,
    IReadOnlyList<CardPurchaseView> Purchases,
    IReadOnlyList<InvoicePaymentView> Payments,
    YearMonth PreviousMonth,
    YearMonth NextMonth);

/// <summary>Persistência dos pagamentos de fatura e leitura da fatura completa (003 US5, research R2, R4).</summary>
public interface IInvoiceStore
{
    /// <summary>A fatura do cartão no mês, exista a linha ou não (research R2), com totais e situação calculados em <paramref name="today"/>.</summary>
    Task<InvoiceDetailView> GetDetailAsync(CreditCard card, YearMonth dueMonth, DateOnly today, CancellationToken cancellationToken);

    /// <summary>
    /// Bloqueia a linha da fatura até o fim da transação em andamento (<c>SELECT … FOR UPDATE</c>, research R4): pagamentos e
    /// "levar restante" da mesma fatura passam a ser serializados.
    /// </summary>
    Task LockAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Pagamento rastreado, ou <c>null</c> se não existe no Espaço.</summary>
    Task<InvoicePayment?> FindPaymentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Pagamentos da fatura, rastreados.</summary>
    Task<IReadOnlyList<InvoicePayment>> ListPaymentsAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<InvoicePaymentView?> GetPaymentViewAsync(Guid id, CancellationToken cancellationToken);

    long VersionOf(InvoicePayment payment);

    /// <summary>Exige que a gravação encontre o pagamento ainda nesta versão (senão <c>concurrency_conflict</c>).</summary>
    void ExpectVersion(InvoicePayment payment, long version);

    void Add(InvoicePayment payment);

    /// <summary>Exclusão lógica no lote corrente do contexto.</summary>
    void Remove(InvoicePayment payment);

    Task SaveAsync(CancellationToken cancellationToken);
}
