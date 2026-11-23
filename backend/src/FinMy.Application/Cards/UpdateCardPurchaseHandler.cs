using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>
/// Edita a compra (FR-015, research R7). Em parcela, <c>scope</c> vale para descrição e observação; valor e natureza só
/// afetam a parcela editada; cartão e data afetam todas e redistribuem as faturas (as movidas à mão não mudam de mês),
/// sem a restrição de fatura fechada da mudança de dias do cartão. Exige <c>version</c> (research R11).
/// </summary>
public sealed class UpdateCardPurchaseHandler(ICreditCardStore cards, ICardPurchaseStore purchases, IInvoiceRepository invoices, IClock clock)
{
    public async Task<CardPurchaseView> HandleAsync(UpdateCardPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var purchase = await purchases.FindAsync(command.Id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.NotFound);
        if (purchase.Origin == PurchaseOrigin.CarryOver)
        {
            throw new DomainException(CardPurchaseErrors.CarryOverNotEditable);
        }

        var version = command.Version ?? throw new DomainException(CardPurchaseErrors.VersionRequired);
        var all = CardPurchaseInput.ParseEditAll(command.Scope);
        var cardId = CardPurchaseInput.RequireCardId(command.CreditCardId);
        var card = await CardPurchaseInput.RequireCardAsync(cards, cardId, mustBeActive: cardId != purchase.CreditCardId, cancellationToken);
        var amount = CardPurchaseInput.ParsePositiveAmount(command.Amount);
        var nature = CardPurchaseInput.ParseNature(command.Nature) ?? purchase.Nature;
        var date = CardPurchaseInput.RequireDate(command.Date);

        if (purchases.VersionOf(purchase) != version)
        {
            throw new DomainException(CardPurchaseErrors.ConcurrencyConflict);
        }

        purchases.ExpectVersion(purchase, version);

        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        if (purchase.InstallmentPlanId is { } planId)
        {
            await UpdateInstallmentAsync(command, purchase, planId, card, amount, nature, date, all, cancellationToken);
        }
        else
        {
            await UpdateSingleAsync(command, purchase, card, amount, nature, date, cancellationToken);
        }

        await purchases.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await purchases.GetViewsAsync([purchase.Id], cancellationToken)).Single();
    }

    private async Task UpdateSingleAsync(
        UpdateCardPurchaseCommand command, CardPurchase purchase, CreditCard card, Money amount, PurchaseNature nature, DateOnly date, CancellationToken cancellationToken)
    {
        var oldMonth = (await purchases.GetInvoicesAsync([purchase.InvoiceId], cancellationToken))[purchase.InvoiceId].DueYearMonth;
        var relocated = card.Id != purchase.CreditCardId || date != purchase.PurchaseDate;

        // Compra movida à mão mantém o mês (no cartão novo, se mudou); as demais seguem a regra de fatura (FR-008).
        var month = relocated && !purchase.ManuallyMoved ? InvoiceCycle.InvoiceFor(date, card.ClosingDay, card.DueDay) : oldMonth;
        var invoiceChanged = card.Id != purchase.CreditCardId || month != oldMonth;

        var newSigned = nature == PurchaseNature.Credit ? -amount.Cents : amount.Cents;
        if (newSigned > 0 && (invoiceChanged || newSigned > CardPurchaseInput.Signed(purchase)))
        {
            await RequireConfirmationAsync(command.ConfirmPaidInvoice, [(card.Id, month)], cancellationToken);
        }

        purchase.ChangeDescription(command.Description ?? string.Empty);
        purchase.ChangeAmount(amount);
        purchase.ChangeNature(nature);
        purchase.ChangeNotes(command.Notes);
        purchase.ChangeDate(date);

        if (invoiceChanged)
        {
            purchase.Reassign(card.Id, (await invoices.GetOrCreateAsync(card.Id, month, cancellationToken)).Id);
        }
    }

    private async Task UpdateInstallmentAsync(
        UpdateCardPurchaseCommand command, CardPurchase purchase, Guid planId, CreditCard card, Money amount, PurchaseNature nature, DateOnly date, bool all, CancellationToken cancellationToken)
    {
        var plan = await purchases.FindPlanAsync(planId, cancellationToken) ?? throw new InvalidOperationException("Plano da parcela não encontrado.");
        var parcels = await purchases.ListPlanAsync(planId, cancellationToken);
        var months = (await purchases.GetInvoicesAsync(parcels.Select(p => p.InvoiceId).Distinct().ToList(), cancellationToken))
            .ToDictionary(i => i.Key, i => i.Value.DueYearMonth);

        var cardChanged = card.Id != plan.CreditCardId;
        var relocate = cardChanged || date != plan.PurchaseDate;

        // Fatura de destino de cada parcela quando cartão ou data mudam (FR-015, FR-012, FR-014).
        var moves = new Dictionary<CardPurchase, YearMonth>();
        YearMonth firstInvoice = YearMonth.From(plan.FirstInvoiceDueMonth);
        if (relocate)
        {
            var first = InvoiceCycle.InvoiceFor(date, card.ClosingDay, card.DueDay);
            if (plan.Mode != InstallmentMode.InProgress)
            {
                firstInvoice = first;
            }

            foreach (var parcel in parcels)
            {
                var month = parcel.ManuallyMoved
                    ? months[parcel.InvoiceId]
                    : plan.Mode == InstallmentMode.InProgress
                        ? firstInvoice.AddMonths(parcel.InstallmentNumber!.Value - plan.FirstNumber)
                        : first.AddMonths(parcel.InstallmentNumber!.Value - 1);
                if (cardChanged || month != months[parcel.InvoiceId])
                {
                    moves[parcel] = month;
                }
            }
        }

        var newSigned = nature == PurchaseNature.Credit ? -amount.Cents : amount.Cents;
        var confirm = moves.Where(m => m.Key.Nature == PurchaseNature.Debit).Select(m => (CardId: card.Id, Month: m.Value)).ToList();
        if (newSigned > CardPurchaseInput.Signed(purchase) && !moves.ContainsKey(purchase))
        {
            confirm.Add((purchase.CreditCardId, months[purchase.InvoiceId]));
        }

        await RequireConfirmationAsync(command.ConfirmPaidInvoice, confirm, cancellationToken);

        purchase.ChangeAmount(amount);
        purchase.ChangeNature(nature);

        foreach (var parcel in all ? parcels : [purchase])
        {
            parcel.ChangeDescription(command.Description ?? string.Empty);
            parcel.ChangeNotes(command.Notes);
        }

        if (all)
        {
            plan.ChangeDescription(command.Description ?? string.Empty);
        }

        if (relocate)
        {
            plan.Relocate(card.Id, date, firstInvoice);
            foreach (var parcel in parcels)
            {
                parcel.ChangeDate(date);
                var month = moves.TryGetValue(parcel, out var target) ? target : months[parcel.InvoiceId];
                parcel.Reassign(card.Id, (await invoices.GetOrCreateAsync(card.Id, month, cancellationToken)).Id);
            }
        }
    }

    private async Task RequireConfirmationAsync(bool confirmed, List<(Guid CardId, YearMonth Month)> targets, CancellationToken cancellationToken)
    {
        if (!confirmed && targets.Count > 0
            && await CardPurchaseInput.AnyPaidAsync(cards, purchases, targets, clock.Today, requireClosed: true, cancellationToken))
        {
            throw new DomainException(CardPurchaseErrors.InvoicePaidConfirmationRequired);
        }
    }
}
