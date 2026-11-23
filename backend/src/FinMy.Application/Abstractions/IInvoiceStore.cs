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

/// <summary>Subtotal da fatura por Tag de acerto (004 FR-011): <paramref name="TagId"/> nulo = "Sem tag de acerto"; Débitos − Créditos.</summary>
public sealed record SettlementSubtotalView(Guid? TagId, long Cents);

/// <summary>
/// Fatura completa (contrato <c>Invoice</c>): resumo, conferido, compras, pagamentos e meses vizinhos (US5, US6.2, FR-027).
/// Com filtro de tags (004 FR-004), <paramref name="Purchases"/> traz só as compras filtradas e <paramref name="FilteredTotalCents"/>
/// a soma delas (Débitos − Créditos); sem filtro, <paramref name="FilteredTotalCents"/> é <c>null</c>.
/// <paramref name="SettlementSubtotals"/> sempre considera todas as compras da fatura (SC-004).
/// </summary>
public sealed record InvoiceDetailView(
    InvoiceSummaryView Summary,
    long ReconciledCents,
    bool CanCarryOver,
    IReadOnlyList<CardPurchaseView> Purchases,
    IReadOnlyList<InvoicePaymentView> Payments,
    YearMonth PreviousMonth,
    YearMonth NextMonth,
    long? FilteredTotalCents,
    IReadOnlyList<SettlementSubtotalView> SettlementSubtotals);

/// <summary>Persistência dos pagamentos de fatura e leitura da fatura completa (003 US5, research R2, R4).</summary>
public interface IInvoiceStore
{
    /// <summary>
    /// A fatura do cartão no mês, exista a linha ou não (research R2), com totais e situação calculados em <paramref name="today"/>.
    /// <paramref name="tagIds"/> vazio = sem filtro de tags.
    /// </summary>
    Task<InvoiceDetailView> GetDetailAsync(
        CreditCard card, YearMonth dueMonth, DateOnly today, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);

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
