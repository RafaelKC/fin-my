using FinMy.Domain.Entries;
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
    uint Version);

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
    uint Version);

/// <summary>Aviso de pendências (FR-019): Prevista com Mês de referência anterior ao mês de hoje.</summary>
public sealed record PendingSummary(int Count, long ToReceiveCents, long ToPayCents);

/// <summary>O que o banco devolve para montar a visão do mês; o resumo é calculado pelo <see cref="MonthSummaryCalculator"/>.</summary>
public sealed record MonthData(
    IReadOnlyList<WalletOpening> Wallets,
    IReadOnlyList<EntryTotal> EntryTotals,
    IReadOnlyList<TransferTotal> TransferTotals,
    IReadOnlyList<InvoiceAmount> InvoicePayments,
    IReadOnlyList<MonthEntryItem> Entries,
    IReadOnlyList<MonthTransferItem> Transfers,
    PendingSummary Pending);

/// <summary>Resposta de <c>GET /months/{month}</c> (contracts/openapi.yaml <c>MonthView</c>).</summary>
public sealed record MonthViewResult(
    string Month,
    DateOnly Today,
    IReadOnlyList<Guid>? WalletIds,
    MonthSummary Summary,
    IReadOnlyList<MonthEntryItem> Entries,
    IReadOnlyList<MonthTransferItem> Transfers,
    PendingSummary Pending);
