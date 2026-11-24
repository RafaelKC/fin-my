using FinMy.Api.Endpoints.Cards;
using FinMy.Application.Months;
using FinMy.Domain.Cards;
using FinMy.Domain.Months;

namespace FinMy.Api.Endpoints.Months;

/// <summary>Coluna do resumo (contracts/openapi.yaml <c>SummaryColumn</c>).</summary>
public sealed record SummaryColumnDto(
    long OpeningBalanceCents,
    long WalletsInitialBalanceCents,
    long IncomeCents,
    long ExpenseCents,
    long ResultCents,
    long TransfersInCents,
    long TransfersOutCents,
    long FinalBalanceCents);

public sealed record EntryDto(
    Guid Id,
    string Kind,
    string Description,
    long AmountCents,
    DateOnly Date,
    string ReferenceMonth,
    string Status,
    Guid WalletId,
    string? Notes,
    DateTimeOffset CreatedAt,
    string CreatedVia,
    long Version,
    IReadOnlyList<Guid> TagIds,
    Guid? SettlementTagId,
    Guid? SplitGroupId,
    int SplitCount,
    Guid? RecurrenceId,
    string? RecurrenceMonth,
    bool RecurrenceModified);

public sealed record TransferDto(
    Guid Id,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountCents,
    DateOnly Date,
    string Description,
    string? Notes,
    DateTimeOffset CreatedAt,
    string CreatedVia,
    long Version);

/// <summary>Item de <c>MonthView.invoices</c> (<c>MonthInvoiceLine</c>).</summary>
public sealed record MonthInvoiceDto(
    Guid CreditCardId,
    string DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    Guid? Id,
    string Status,
    long TotalCents,
    long PaidCents,
    long RemainingCents,
    string CreditCardName,
    long PlannedCents,
    long ActualCents);

public sealed record PendingDto(int Count, long ToReceiveCents, long ToPayCents);

/// <summary>Totais dos itens listados com filtro de tags (<c>filterTotals</c>).</summary>
public sealed record FilterTotalsDto(long IncomeCents, long ExpenseCents, long PurchasesNetCents);

/// <summary>Resposta de <c>GET /months/{month}</c> (<c>MonthView</c>).</summary>
public sealed record MonthViewDto(
    string Month,
    DateOnly Today,
    IReadOnlyList<Guid>? WalletIds,
    SummaryColumnDto Planned,
    SummaryColumnDto Actual,
    IReadOnlyList<EntryDto> Entries,
    IReadOnlyList<TransferDto> Transfers,
    IReadOnlyList<MonthInvoiceDto> Invoices,
    PendingDto Pending,
    IReadOnlyList<CardPurchaseResponse> Purchases,
    FilterTotalsDto? FilterTotals)
{
    public static MonthViewDto From(MonthViewResult r) => new(
        r.Month,
        r.Today,
        r.WalletIds,
        From(r.Summary.Planned),
        From(r.Summary.Actual),
        r.Entries.Select(From).ToList(),
        r.Transfers.Select(From).ToList(),
        r.Invoices.Select(From).ToList(),
        new PendingDto(r.Pending.Count, r.Pending.ToReceiveCents, r.Pending.ToPayCents),
        r.Purchases.Select(CardPurchaseResponse.From).ToList(),
        r.FilterTotals is { } t ? new FilterTotalsDto(t.IncomeCents, t.ExpenseCents, t.PurchasesNetCents) : null);

    private static SummaryColumnDto From(SummaryColumn c) => new(
        c.OpeningBalanceCents,
        c.WalletsInitialBalanceCents,
        c.IncomeCents,
        c.ExpenseCents,
        c.ResultCents,
        c.TransfersInCents,
        c.TransfersOutCents,
        c.FinalBalanceCents);

    private static EntryDto From(MonthEntryItem e) => new(
        e.Id,
        SnakeCase(e.Kind.ToString()),
        e.Description,
        e.AmountCents,
        e.Date,
        e.ReferenceMonth.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
        SnakeCase(e.Status.ToString()),
        e.WalletId,
        e.Notes,
        e.CreatedAt,
        ChannelText.Of(e.CreatedVia),
        e.Version,
        e.TagIds,
        e.SettlementTagId,
        e.SplitGroupId,
        e.SplitCount,
        e.RecurrenceId,
        e.RecurrenceMonth?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
        e.RecurrenceModified);

    private static TransferDto From(MonthTransferItem t) => new(
        t.Id, t.FromWalletId, t.ToWalletId, t.AmountCents, t.Date, t.Description, t.Notes, t.CreatedAt, ChannelText.Of(t.CreatedVia), t.Version);

    private static MonthInvoiceDto From(MonthInvoiceItem i) => new(
        i.CreditCardId,
        i.DueMonth.ToString(),
        i.ClosingDate,
        i.DueDate,
        i.Id,
        i.Status.ToCode(),
        i.TotalCents,
        i.PaidCents,
        i.RemainingCents,
        i.CreditCardName,
        i.PlannedCents,
        i.ActualCents);

    private static string SnakeCase(string name) => name.ToLowerInvariant();
}
