using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>
/// Faturas de um cartão num intervalo de meses de vencimento (008 <c>list_invoices</c>): cada uma é a mesma fatura de
/// <see cref="GetInvoiceHandler"/>. Padrão: 3 meses antes e 3 depois da fatura atual; no máximo <see cref="MaxMonths"/> meses.
/// </summary>
public sealed class ListInvoicesHandler(ICreditCardStore cards, IInvoiceStore invoices, IClock clock)
{
    public const int MaxMonths = 24;
    public const int DefaultSpan = 3;

    public async Task<IReadOnlyList<InvoiceSummaryView>> HandleAsync(Guid creditCardId, string? from, string? to, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(creditCardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);

        var today = clock.Today;
        var center = from is null || to is null
            ? (await cards.GetOverviewAsync(creditCardId, today, cancellationToken))?.CurrentInvoice.DueMonth ?? YearMonth.From(today)
            : YearMonth.From(today);
        var first = from is null ? center.AddMonths(-DefaultSpan) : InvoiceInput.ParseMonth(from);
        var last = to is null ? center.AddMonths(DefaultSpan) : InvoiceInput.ParseMonth(to);

        if (first > last)
        {
            throw new DomainException(InvoiceErrors.Field("period.invalid", "to", "O mês final deve ser igual ou posterior ao inicial."));
        }

        var months = ((last.Year - first.Year) * 12) + last.Month - first.Month + 1;
        if (months > MaxMonths)
        {
            throw new DomainException(InvoiceErrors.Field("period.too_long", "to", $"Peça no máximo {MaxMonths} meses de faturas por vez."));
        }

        var result = new List<InvoiceSummaryView>(months);
        for (var month = first; month <= last; month = month.Next())
        {
            result.Add((await invoices.GetDetailAsync(card, month, today, [], cancellationToken)).Summary);
        }

        return result;
    }
}
