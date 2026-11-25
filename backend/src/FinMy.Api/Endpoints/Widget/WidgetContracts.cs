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
