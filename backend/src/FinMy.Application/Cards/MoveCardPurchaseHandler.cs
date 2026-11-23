using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;

namespace FinMy.Application.Cards;

/// <summary>
/// Move a compra para a fatura anterior ou seguinte do mesmo cartão (FR-028); em parcela, só esta ou esta e as
/// seguintes, cada uma uma fatura. As movidas ficam marcadas e a mudança de dias do cartão não as redistribui.
/// </summary>
public sealed class MoveCardPurchaseHandler(ICardPurchaseStore purchases, IInvoiceRepository invoices)
{
    public async Task<IReadOnlyList<CardPurchaseView>> HandleAsync(MoveCardPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var purchase = await purchases.FindAsync(command.Id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.NotFound);
        var step = command.Direction switch
        {
            "previous" => -1,
            "next" => 1,
            _ => throw new DomainException(CardPurchaseErrors.InvalidDirection),
        };
        var scope = CardPurchaseInput.ParseScope(command.Scope, allowAll: false);

        IReadOnlyList<CardPurchase> moving = [purchase];
        if (scope == PurchaseScope.ThisAndFollowing && purchase.InstallmentPlanId is { } planId)
        {
            moving = (await purchases.ListPlanAsync(planId, cancellationToken))
                .Where(p => p.InstallmentNumber >= purchase.InstallmentNumber)
                .ToList();
        }

        var current = await purchases.GetInvoicesAsync(moving.Select(p => p.InvoiceId).Distinct().ToList(), cancellationToken);
        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        foreach (var parcel in moving)
        {
            var target = await invoices.GetOrCreateAsync(parcel.CreditCardId, current[parcel.InvoiceId].DueYearMonth.AddMonths(step), cancellationToken);
            parcel.MoveToInvoice(target.Id);
        }

        await purchases.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await purchases.GetViewsAsync(moving.Select(p => p.Id).ToList(), cancellationToken);
    }
}
