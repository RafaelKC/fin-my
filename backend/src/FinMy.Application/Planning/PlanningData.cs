using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;

namespace FinMy.Application.Planning;

/// <summary>Lançamento com Mês de referência no ano (a grade ignora Compras e transferências).</summary>
public sealed record PlanningEntry(
    Guid Id,
    EntryKind Kind,
    string Description,
    string DescriptionNormalized,
    long AmountCents,
    DateOnly Date,
    YearMonth Month,
    EntryStatus Status,
    Guid WalletId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> TagIds);

public sealed record PlanningCard(Guid Id, string Name, bool Archived);

/// <summary>Fatura do cartão com vencimento no ano: Previsto (com estimativa quando vale), total lançado e a estimativa gravada.</summary>
public sealed record PlanningInvoice(Guid CreditCardId, YearMonth DueMonth, long PlannedCents, long TotalCents, long? EstimateCents);

public sealed record PlanningData(
    IReadOnlyList<PlanningEntry> Entries,
    IReadOnlySet<Guid> ActiveWalletIds,
    IReadOnlyList<PlanningCard> Cards,
    IReadOnlyList<PlanningInvoice> Invoices);

/// <summary>Célula de Entrada/Saída (research R2): <c>EntryId</c> só quando há exatamente um item.</summary>
public sealed record PlanningCell(YearMonth Month, long PlannedCents, int ItemCount, bool AllSettled, bool Editable, Guid? EntryId);

public sealed record PlanningRow(
    EntryKind Kind,
    string Key,
    string Description,
    Guid? LatestWalletId,
    int LatestDay,
    IReadOnlyList<Guid> LatestTagIds,
    IReadOnlyList<PlanningCell> Cells);

public sealed record InvoiceCell(YearMonth Month, long PlannedCents, long TotalCents, long? EstimateCents);

public sealed record InvoiceRow(Guid CreditCardId, string CreditCardName, bool Archived, IReadOnlyList<InvoiceCell> Cells);

/// <summary>Totais Previstos de um mês: os mesmos do painel (006) e da visão do mês.</summary>
public sealed record PlanningTotalsMonth(YearMonth Month, long IncomeCents, long ExpenseCents, long ResultCents, long FinalBalanceCents);

public sealed record PlanningGridResult(
    int Year,
    DateOnly Today,
    IReadOnlyList<PlanningRow> IncomeRows,
    IReadOnlyList<PlanningRow> ExpenseRows,
    IReadOnlyList<InvoiceRow> InvoiceRows,
    IReadOnlyList<PlanningTotalsMonth> Totals);
