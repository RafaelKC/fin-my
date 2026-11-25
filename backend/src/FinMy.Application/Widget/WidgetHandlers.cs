using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;

namespace FinMy.Application.Widget;

/// <summary>Erros do widget Android (009 contracts/api.md).</summary>
public static class WidgetErrors
{
    /// <summary>Cartão de outro Espaço, arquivado ou excluído: o widget não distingue (mostra "Cartão indisponível").</summary>
    public static readonly DomainError CreditCardNotFound = new("credit_card.not_found", "Cartão não encontrado.", DomainErrorKind.NotFound);
}

/// <summary>Cartão da lista de escolha do widget.</summary>
public sealed record WidgetCardView(Guid Id, string Name, string Color, int ClosingDay, int DueDay);

public sealed record WidgetCardsView(DateOnly Today, IReadOnlyList<WidgetCardView> Cards);

/// <summary>Fatura atual de um cartão com os números que o widget mostra (009 research R4).</summary>
public sealed record WidgetInvoiceView(
    DateOnly Today,
    Guid CardId,
    string CardName,
    string CardColor,
    string DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    string Status,
    long TotalCents,
    long PlannedCents,
    long? EstimateCents,
    long PaidCents,
    long RemainingCents);

/// <summary>Cartões ativos do Espaço, por nome (somente leitura).</summary>
public sealed class ListWidgetCardsHandler(ICreditCardStore cards, IClock clock)
{
    public async Task<WidgetCardsView> HandleAsync(CancellationToken cancellationToken)
    {
        var overviews = await cards.ListOverviewsAsync(CreditCardArchivedFilter.ActiveOnly, clock.Today, cancellationToken);
        var items = overviews
            .Select(o => o.Card)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id)
            .Select(c => new WidgetCardView(c.Id, c.Name, c.Color, c.ClosingDay, c.DueDay))
            .ToList();
        return new WidgetCardsView(clock.Today, items);
    }
}

/// <summary>
/// "Fatura atual" = a em que cairia uma compra feita hoje (mesma regra da tela do cartão). Os valores vêm do mesmo handler da
/// tela da fatura (<see cref="GetInvoiceHandler"/>) e o Planejado da mesma regra da 007 (<see cref="InvoiceForecast"/>), logo
/// idênticos aos do app (SC-002).
/// </summary>
public sealed class GetWidgetCurrentInvoiceHandler(GetCreditCardHandler getCard, GetInvoiceHandler getInvoice, IClock clock)
{
    public async Task<WidgetInvoiceView> HandleAsync(Guid creditCardId, CancellationToken cancellationToken)
    {
        var overview = await FindActiveAsync(creditCardId, cancellationToken);
        var detail = await getInvoice.HandleAsync(creditCardId, overview.CurrentInvoice.DueMonth.ToString(), null, cancellationToken);
        var invoice = detail.Summary;
        var today = clock.Today;

        return new WidgetInvoiceView(
            today,
            overview.Card.Id,
            overview.Card.Name,
            overview.Card.Color,
            invoice.DueMonth.ToString(),
            invoice.ClosingDate,
            invoice.DueDate,
            invoice.Status.ToCode(),
            invoice.TotalCents,
            InvoiceForecast.Planned(invoice.TotalCents, invoice.PaidCents, invoice.EstimateCents, today, invoice.ClosingDate),
            invoice.EstimateActive ? invoice.EstimateCents : null,
            invoice.PaidCents,
            invoice.RemainingCents);
    }

    private async Task<CreditCardOverviewView> FindActiveAsync(Guid creditCardId, CancellationToken cancellationToken)
    {
        try
        {
            var overview = await getCard.HandleAsync(creditCardId, cancellationToken);
            return overview.Card.Archived ? throw new DomainException(WidgetErrors.CreditCardNotFound) : overview;
        }
        catch (DomainException exception) when (exception.Error == CreditCardErrors.NotFound)
        {
            throw new DomainException(WidgetErrors.CreditCardNotFound);
        }
    }
}
