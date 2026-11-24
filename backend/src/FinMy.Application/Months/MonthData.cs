using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;

namespace FinMy.Application.Months;

/// <summary>Lançamento da lista do mês (contracts/openapi.yaml <c>Entry</c>).</summary>
public sealed record MonthEntryItem(
    Guid Id,
    EntryKind Kind,
    string Description,
    long AmountCents,
    DateOnly Date,
    DateOnly ReferenceMonth,
    EntryStatus Status,
    Guid WalletId,
    string? Notes,
    DateTimeOffset CreatedAt,
    Channel CreatedVia,
    uint Version,
    IReadOnlyList<Guid> TagIds,
    Guid? SettlementTagId,
    Guid? SplitGroupId,
    int SplitCount,
    Guid? RecurrenceId,
    DateOnly? RecurrenceMonth,
    bool RecurrenceModified);

/// <summary>Transferência da lista do mês (contracts/openapi.yaml <c>Transfer</c>).</summary>
public sealed record MonthTransferItem(
    Guid Id,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountCents,
    DateOnly Date,
    string Description,
    string? Notes,
    DateTimeOffset CreatedAt,
    Channel CreatedVia,
    uint Version);

/// <summary>Aviso de pendências (FR-019): Prevista com Mês de referência anterior ao mês de hoje.</summary>
public sealed record PendingSummary(int Count, long ToReceiveCents, long ToPayCents);

/// <summary>
/// Linha "Fatura &lt;Cartão&gt;" da lista do mês (contracts/openapi.yaml <c>MonthInvoiceLine</c>, 003 FR-021): total, pago e restante
/// da fatura inteira; <see cref="PlannedCents"/> e <see cref="ActualCents"/> já no recorte do filtro de Carteiras (research R8).
/// </summary>
public sealed record MonthInvoiceItem(
    Guid CreditCardId,
    string CreditCardName,
    YearMonth DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    Guid? Id,
    InvoiceStatus Status,
    long TotalCents,
    long PaidCents,
    long RemainingCents,
    long PlannedCents,
    long ActualCents);

/// <summary>O que o banco devolve para montar a visão do mês; o resumo é calculado pelo <see cref="MonthSummaryCalculator"/>.</summary>
public sealed record MonthData(
    IReadOnlyList<WalletOpening> Wallets,
    IReadOnlyList<EntryTotal> EntryTotals,
    IReadOnlyList<TransferTotal> TransferTotals,
    IReadOnlyList<InvoiceFigure> InvoiceFigures,
    IReadOnlyList<MonthEntryItem> Entries,
    IReadOnlyList<MonthTransferItem> Transfers,
    IReadOnlyList<MonthInvoiceItem> Invoices,
    PendingSummary Pending,
    IReadOnlyList<CardPurchaseView> Purchases);

/// <summary>Totais dos itens listados com filtro de tags (004 research R6): Entradas, Saídas e Compras (Débitos − Créditos).</summary>
public sealed record MonthFilterTotals(long IncomeCents, long ExpenseCents, long PurchasesNetCents);

/// <summary>
/// Resposta de <c>GET /months/{month}</c> (contracts/openapi.yaml <c>MonthView</c>). Com filtro de tags, <see cref="Purchases"/>
/// traz as compras das faturas que vencem no mês e <see cref="FilterTotals"/> a soma do que foi listado; sem filtro ficam
/// vazio e <c>null</c> (as compras aparecem nas faturas).
/// </summary>
public sealed record MonthViewResult(
    string Month,
    DateOnly Today,
    IReadOnlyList<Guid>? WalletIds,
    MonthSummary Summary,
    IReadOnlyList<MonthEntryItem> Entries,
    IReadOnlyList<MonthTransferItem> Transfers,
    IReadOnlyList<MonthInvoiceItem> Invoices,
    PendingSummary Pending,
    IReadOnlyList<CardPurchaseView> Purchases,
    MonthFilterTotals? FilterTotals);
