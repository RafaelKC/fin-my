using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>
/// Edita o cartão exigindo <c>version</c> (research R11). Mudar fechamento ou vencimento redistribui as faturas
/// futuras e suas compras na mesma transação (research R3, Edge Case da spec): só faturas cujo fechamento gravado
/// e novo são posteriores a hoje, só compras que não foram movidas manualmente e nunca para uma fatura já fechada.
/// </summary>
public sealed class UpdateCreditCardHandler(ICreditCardStore cards, IWalletStore wallets, IInvoiceRepository invoices, IClock clock)
{
    public async Task<CreditCardView> HandleAsync(Guid id, CreditCardInput input, long? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var card = await cards.FindAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);

        var parsed = CreditCardInputParser.Parse(input);
        if (version is null)
        {
            throw new DomainException(CreditCardErrors.VersionRequired);
        }

        if (cards.VersionOf(card) != version)
        {
            throw new DomainException(CreditCardErrors.ConcurrencyConflict);
        }

        // Só troca de carteira exige carteira ativa: manter uma arquivada não impede editar o resto.
        if (parsed.PaymentWalletId != card.PaymentWalletId)
        {
            await CreditCardWallets.EnsureActiveAsync(wallets, parsed.PaymentWalletId, cancellationToken);
        }

        var daysChanged = parsed.ClosingDay != card.ClosingDay || parsed.DueDay != card.DueDay;

        cards.ExpectVersion(card, version.Value);
        card.Rename(parsed.Name);
        card.ChangeColor(parsed.Color);
        card.ChangeDays(parsed.ClosingDay, parsed.DueDay);
        card.ChangePaymentWallet(parsed.PaymentWalletId);
        card.ChangeLimit(parsed.Limit);

        if (daysChanged)
        {
            await using var transaction = await cards.BeginTransactionAsync(cancellationToken);

            // O cartão é gravado antes: GetOrCreate lê os dias novos do banco ao criar linhas de fatura.
            await cards.SaveAsync(cancellationToken);
            await RedistributeAsync(card, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await cards.SaveAsync(cancellationToken);
        }

        return await cards.GetAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
    }

    private async Task RedistributeAsync(CreditCard card, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var all = (await cards.ListInvoicesAsync(card.Id, cancellationToken)).ToList();

        // Faturas ainda abertas (fechamento gravado > hoje) cujo novo fechamento também é posterior a hoje.
        var affected = all
            .Where(i => i.ClosingDate > today && InvoiceCycle.ClosingDate(i.DueYearMonth, card.ClosingDay, card.DueDay) > today)
            .ToList();
        if (affected.Count == 0)
        {
            return;
        }

        foreach (var invoice in affected)
        {
            invoice.ChangeDates(
                InvoiceCycle.ClosingDate(invoice.DueYearMonth, card.ClosingDay, card.DueDay),
                InvoiceCycle.DueDate(invoice.DueYearMonth, card.DueDay));
        }

        var purchases = (await cards.ListPurchasesAsync(affected.Select(i => i.Id).ToList(), cancellationToken))
            .Where(p => !p.ManuallyMoved && p.Origin != PurchaseOrigin.CarryOver)
            .ToList();
        var plans = await cards.GetPlansAsync(
            purchases.Where(p => p.InstallmentPlanId is not null).Select(p => p.InstallmentPlanId!.Value).Distinct().ToList(),
            cancellationToken);

        // Fechamento vigente por mês: o da linha existente (gravado) ou o calculado com os dias novos.
        var known = all.ToDictionary(i => i.DueYearMonth, i => i.ClosingDate);
        var resolved = new Dictionary<YearMonth, Guid>();

        foreach (var purchase in purchases)
        {
            var target = TargetMonth(purchase, plans, card);
            if (target is null)
            {
                continue;
            }

            // Nenhuma compra vai para fatura já fechada: segue para a primeira ainda aberta (a atual).
            var month = target.Value;
            while (ClosingOf(month, known, card) <= today)
            {
                month = month.Next();
            }

            if (!resolved.TryGetValue(month, out var invoiceId))
            {
                invoiceId = (await invoices.GetOrCreateAsync(card.Id, month, cancellationToken)).Id;
                resolved[month] = invoiceId;
            }

            purchase.Redistribute(invoiceId);
        }

        await cards.SaveAsync(cancellationToken);
    }

    private static DateOnly ClosingOf(YearMonth month, Dictionary<YearMonth, DateOnly> known, CreditCard card) =>
        known.TryGetValue(month, out var stored) ? stored : InvoiceCycle.ClosingDate(month, card.ClosingDay, card.DueDay);

    /// <summary>Fatura de destino pela regra (FR-008, FR-012); <c>null</c> quando a compra não segue os dias do cartão.</summary>
    private static YearMonth? TargetMonth(CardPurchase purchase, IReadOnlyDictionary<Guid, InstallmentPlan> plans, CreditCard card)
    {
        if (purchase.InstallmentPlanId is not { } planId || purchase.InstallmentNumber is not { } number)
        {
            return InvoiceCycle.InvoiceFor(purchase.PurchaseDate, card.ClosingDay, card.DueDay);
        }

        // Parcelamento em andamento: a sequência parte da fatura escolhida e não depende dos dias.
        return plans[planId].Mode == InstallmentMode.InProgress
            ? null
            : InvoiceCycle.InvoiceFor(purchase.PurchaseDate, card.ClosingDay, card.DueDay).AddMonths(number - 1);
    }
}
