using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>
/// Altera N ou o valor do parcelamento recriando as parcelas (FR-015, research R7): as antigas são excluídas (lógico, no
/// mesmo lote) e as novas criadas a partir da data e do cartão do plano. Recusado se alguma parcela está em fatura Paga.
/// Parcelamento em andamento só aceita o valor da parcela e mantém a próxima parcela p e a fatura da p.
/// </summary>
public sealed class UpdateInstallmentPlanHandler(ICreditCardStore cards, ICardPurchaseStore purchases, IInvoiceRepository invoices, IClock clock, ICurrentContext context)
{
    public async Task<IReadOnlyList<CardPurchaseView>> HandleAsync(UpdateInstallmentPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await purchases.FindPlanAsync(command.Id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.PlanNotFound);
        var mode = command.AmountMode switch
        {
            "total_amount" => InstallmentMode.TotalAmount,
            "installment_amount" => InstallmentMode.InstallmentAmount,
            _ => throw new DomainException(CardPurchaseErrors.InvalidAmountMode),
        };
        if (plan.Mode == InstallmentMode.InProgress)
        {
            if (mode != InstallmentMode.InstallmentAmount)
            {
                throw new DomainException(CardPurchaseErrors.InProgressNeedsInstallmentAmount);
            }

            mode = InstallmentMode.InProgress;
        }

        var total = command.InstallmentTotal ?? throw new DomainException(CardPurchaseErrors.InstallmentsTotalRequired);
        var amount = CardPurchaseInput.ParsePositiveAmount(command.Amount);

        var card = await cards.FindAsync(plan.CreditCardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        var old = await purchases.ListPlanAsync(plan.Id, cancellationToken);
        var oldInvoices = (await purchases.GetInvoicesAsync(old.Select(p => p.InvoiceId).Distinct().ToList(), cancellationToken)).Values.ToList();
        var totals = await purchases.GetInvoiceTotalsAsync(oldInvoices.Select(i => i.Id).ToList(), cancellationToken);
        if (oldInvoices.Any(i =>
            {
                var t = totals.GetValueOrDefault(i.Id);
                return InvoiceStatusRule.Evaluate(t.TotalCents, t.PaidCents, clock.Today, i.ClosingDate, i.DueDate) == InvoiceStatus.Paid;
            }))
        {
            throw new DomainException(CardPurchaseErrors.PlanHasPaidInvoice);
        }

        plan.Reconfigure(mode, total, amount);
        var first = plan.Mode == InstallmentMode.InProgress
            ? YearMonth.From(plan.FirstInvoiceDueMonth)
            : InvoiceCycle.InvoiceFor(plan.PurchaseDate, card.ClosingDay, card.DueDay);
        plan.Relocate(plan.CreditCardId, plan.PurchaseDate, first);

        var amountCents = plan.Mode == InstallmentMode.TotalAmount ? plan.TotalAmountCents!.Value : plan.InstallmentAmountCents!.Value;
        var generated = InstallmentGenerator.Generate(plan.Mode, plan.InstallmentTotal, plan.FirstNumber, amountCents, first);
        var notes = old.Select(p => p.Notes).FirstOrDefault(n => n is not null);

        context.BeginDeletionBatch();
        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        foreach (var parcel in old)
        {
            purchases.Remove(parcel);
        }

        var created = new List<CardPurchase>();
        foreach (var item in generated)
        {
            var invoice = await invoices.GetOrCreateAsync(card.Id, item.DueMonth, cancellationToken);
            var parcel = CardPurchase.CreateInstallment(card.Id, invoice.Id, plan, item.Number, Money.FromCents(item.AmountCents), notes);
            purchases.Add(parcel);
            created.Add(parcel);
        }

        await purchases.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await purchases.GetViewsAsync(created.Select(p => p.Id).ToList(), cancellationToken);
    }
}
