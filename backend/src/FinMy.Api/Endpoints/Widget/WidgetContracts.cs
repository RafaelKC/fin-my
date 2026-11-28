using FinMy.Application.Widget;
using FinMy.Domain.Cards;

namespace FinMy.Api.Endpoints.Widget;

/// <summary>Cartão da escolha do widget (009 contracts/api.md).</summary>
public sealed record WidgetCardResponse(Guid Id, string Name, string Color, int ClosingDay, int DueDay);

/// <summary>Resposta de <c>GET /api/v1/widget/credit-cards</c>.</summary>
public sealed record WidgetCardsResponse(DateOnly Today, IReadOnlyList<WidgetCardResponse> Cards)
{
    public static WidgetCardsResponse From(WidgetCardsView v) =>
        new(v.Today, v.Cards.Select(c => new WidgetCardResponse(c.Id, c.Name, c.Color, c.ClosingDay, c.DueDay)).ToList());
}

public sealed record WidgetCardRef(Guid Id, string Name, string Color);

public sealed record WidgetInvoiceResponse(
    string DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    string Status,
    long TotalCents,
    string TotalText,
    long PlannedCents,
    string PlannedText,
    long? EstimateCents,
    long PaidCents,
    long RemainingCents,
    string RemainingText);

/// <summary>Total de uma fatura no gráfico do widget.</summary>
public sealed record WidgetHistoryResponse(string DueMonth, long TotalCents);

/// <summary>Resposta de <c>GET /api/v1/widget/credit-cards/{id}/current-invoice</c>; <c>History</c> = as últimas faturas até a atual.</summary>
public sealed record WidgetCurrentInvoiceResponse(
    DateOnly Today, WidgetCardRef Card, WidgetInvoiceResponse Invoice, string AppPath, IReadOnlyList<WidgetHistoryResponse> History)
{
    public static WidgetCurrentInvoiceResponse From(WidgetInvoiceView v) => new(
        v.Today,
        new WidgetCardRef(v.CardId, v.CardName, v.CardColor),
        new WidgetInvoiceResponse(
            v.DueMonth, v.ClosingDate, v.DueDate, v.Status,
            v.TotalCents, FinMy.Application.Cards.InvoiceErrors.FormatBrl(v.TotalCents),
            v.PlannedCents, FinMy.Application.Cards.InvoiceErrors.FormatBrl(v.PlannedCents),
            v.EstimateCents, v.PaidCents,
            v.RemainingCents, FinMy.Application.Cards.InvoiceErrors.FormatBrl(v.RemainingCents)),
        $"/cartoes/{v.CardId}/faturas/{v.DueMonth}",
        v.History.Select(h => new WidgetHistoryResponse(h.DueMonth, h.TotalCents)).ToList());
}

/// <summary>Resposta de <c>GET /api/v1/widget/overview</c> (010): valores em centavos e já formatados.</summary>
public sealed record WidgetOverviewResponse(
    DateOnly Today,
    string Month,
    WidgetMoney PlannedIncome,
    WidgetMoney PlannedExpense,
    WidgetMoney PlannedFinal,
    WidgetMoney ActualIncome,
    WidgetMoney ActualExpense,
    WidgetMoney ActualFinal,
    WidgetMoney TotalBalance,
    WidgetPendingResponse Pending,
    IReadOnlyList<WidgetUpcomingResponse> Upcoming,
    IReadOnlyList<WidgetSettlementResponse> Settlements,
    string MonthPath,
    string SettlementsPath)
{
    public static WidgetOverviewResponse From(WidgetOverviewView v) => new(
        v.Today,
        v.Month,
        WidgetMoney.Of(v.Figures.PlannedIncomeCents),
        WidgetMoney.Of(v.Figures.PlannedExpenseCents),
        WidgetMoney.Of(v.Figures.PlannedFinalCents),
        WidgetMoney.Of(v.Figures.ActualIncomeCents),
        WidgetMoney.Of(v.Figures.ActualExpenseCents),
        WidgetMoney.Of(v.Figures.ActualFinalCents),
        WidgetMoney.Of(v.TotalBalanceCents),
        new WidgetPendingResponse(v.Pending.Count, WidgetMoney.Of(v.Pending.ToReceiveCents), WidgetMoney.Of(v.Pending.ToPayCents)),
        v.Upcoming.Select(u => new WidgetUpcomingResponse(u.Kind, u.Description, u.Date, WidgetMoney.Of(u.AmountCents), u.Overdue, u.EntryId)).ToList(),
        v.Settlements.Select(s => new WidgetSettlementResponse(s.TagId, s.Name, s.Color, WidgetMoney.Of(s.AmountCents))).ToList(),
        $"/mes/{v.Month}",
        $"/acertos/{v.Month}");
}

/// <summary>Valor em centavos com o texto pt-BR (o mesmo formato das telas).</summary>
public sealed record WidgetMoney(long Cents, string Text)
{
    public static WidgetMoney Of(long cents) => new(cents, FinMy.Application.Cards.InvoiceErrors.FormatBrl(cents));
}

public sealed record WidgetPendingResponse(int Count, WidgetMoney ToReceive, WidgetMoney ToPay);

public sealed record WidgetUpcomingResponse(string Kind, string Description, DateOnly Date, WidgetMoney Amount, bool Overdue, Guid? EntryId);

public sealed record WidgetSettlementResponse(Guid TagId, string Name, string Color, WidgetMoney Amount);

/// <summary>Corpo de <c>POST /api/v1/widget/entries</c> (relógio, 010).</summary>
public sealed record WidgetCreateEntryRequest(string? Kind, string? Description, string? Amount);

/// <summary>Resposta do lançamento rápido do relógio.</summary>
public sealed record WidgetCreatedEntryResponse(Guid Id, string Kind, string Description, WidgetMoney Amount, DateOnly Date, string Status, string Wallet)
{
    public static WidgetCreatedEntryResponse From(WidgetCreatedEntry e) =>
        new(e.Id, e.Kind, e.Description, WidgetMoney.Of(e.AmountCents), e.Date, e.Status, e.WalletName);
}

/// <summary>Corpo de <c>POST /api/v1/widget/suggestions</c>: notificação de banco lida no celular (010).</summary>
public sealed record WidgetSuggestionRequest(
    string? Source, string? Bank, string? Kind, string? Description, long AmountCents, DateTimeOffset? OccurredAt,
    string? CardLast4, string? RawText, string? DedupeKey)
{
    public FinMy.Application.Suggestions.SubmitSuggestionInput ToInput() =>
        new(Source, Bank, Kind, Description, AmountCents, OccurredAt, CardLast4, RawText, DedupeKey);
}
