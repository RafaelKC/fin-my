using FinMy.Application.Months;
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
    long Version);

public sealed record TransferDto(
    Guid Id,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountCents,
    DateOnly Date,
    string Description,
    string? Notes,
    DateTimeOffset CreatedAt,
    long Version);

public sealed record PendingDto(int Count, long ToReceiveCents, long ToPayCents);

/// <summary>Resposta de <c>GET /months/{month}</c> (<c>MonthView</c>).</summary>
public sealed record MonthViewDto(
    string Month,
    DateOnly Today,
    IReadOnlyList<Guid>? WalletIds,
    SummaryColumnDto Planned,
    SummaryColumnDto Actual,
    IReadOnlyList<EntryDto> Entries,
    IReadOnlyList<TransferDto> Transfers,
    PendingDto Pending)
{
    public static MonthViewDto From(MonthViewResult r) => new(
        r.Month,
        r.Today,
        r.WalletIds,
        From(r.Summary.Planned),
        From(r.Summary.Actual),
        r.Entries.Select(From).ToList(),
        r.Transfers.Select(From).ToList(),
        new PendingDto(r.Pending.Count, r.Pending.ToReceiveCents, r.Pending.ToPayCents));

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
        e.Version);

    private static TransferDto From(MonthTransferItem t) => new(
        t.Id, t.FromWalletId, t.ToWalletId, t.AmountCents, t.Date, t.Description, t.Notes, t.CreatedAt, t.Version);

    private static string SnakeCase(string name) => name.ToLowerInvariant();
}
