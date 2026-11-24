using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Exports;

public enum ExportRowType
{
    Income,
    Expense,
    Purchase,
    InvoicePayment,
    Transfer,
}

/// <summary>Uma linha da exportação CSV, já com o valor com sinal e o mês em que entra no período.</summary>
public sealed record ExportRow(
    ExportRowType Type,
    string Description,
    DateOnly Date,
    YearMonth PeriodMonth,
    YearMonth? ReferenceMonth,
    long AmountCents,
    EntryStatus? Status,
    string Wallet,
    string? Card,
    YearMonth? Invoice,
    string? Installment,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid Id);
