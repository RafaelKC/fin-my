using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>
/// Cria compra à vista, parcelada (valor total ou da parcela) ou parcelamento em andamento (US2–US4). A fatura de cada
/// parcela vem do ciclo do cartão (FR-008, FR-012, FR-014); valor negativo vira crédito (research R6); compra que reabre
/// fatura paga pede confirmação (research R5). Tudo numa transação.
/// </summary>
public sealed class CreateCardPurchaseHandler(ICreditCardStore cards, ICardPurchaseStore purchases, IInvoiceRepository invoices, ITagStore tags, IClock clock)
{
    private sealed record Target(int Number, Money Amount, YearMonth Month);

    public async Task<CardPurchaseCreateResult> HandleAsync(CreateCardPurchaseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var card = await CardPurchaseInput.RequireCardAsync(cards, CardPurchaseInput.RequireCardId(command.CreditCardId), mustBeActive: true, cancellationToken);
        var date = CardPurchaseInput.RequireDate(command.Date);
        var (amount, negative) = CardPurchaseInput.ParseSignedAmount(command.Amount);
        var nature = negative ? PurchaseNature.Credit : CardPurchaseInput.ParseNature(command.Nature) ?? PurchaseNature.Debit;
        var warnings = negative ? new[] { CardPurchaseInput.RecordedAsCredit } : [];

        var description = command.Description ?? string.Empty;
        var installments = command.Installments;
        var inProgress = installments is not null && CardPurchaseInput.ParseMode(installments.Mode) == InstallmentMode.InProgress;
        var single = installments is null || (!inProgress && installments.Total == 1);

        if (nature == PurchaseNature.Credit && !single)
        {
            throw new DomainException(CardPurchaseErrors.CreditMustBeSingle);
        }

        InstallmentPlan? plan = null;
        var targets = new List<Target>();
        if (single)
        {
            targets.Add(new Target(0, amount, InvoiceCycle.InvoiceFor(date, card.ClosingDay, card.DueDay)));
        }
        else
        {
            plan = BuildPlan(card, description, installments!, amount, date);
            var first = YearMonth.From(plan.FirstInvoiceDueMonth);
            var amountCents = plan.Mode == InstallmentMode.TotalAmount ? plan.TotalAmountCents!.Value : plan.InstallmentAmountCents!.Value;
            targets.AddRange(InstallmentGenerator
                .Generate(plan.Mode, plan.InstallmentTotal, plan.FirstNumber, amountCents, first)
                .Select(g => new Target(g.Number, Money.FromCents(g.AmountCents), g.DueMonth)));
        }

        if (nature == PurchaseNature.Debit && !command.ConfirmPaidInvoice
            && await CardPurchaseInput.AnyPaidAsync(cards, purchases, targets.Select(t => (card.Id, t.Month)), clock.Today, requireClosed: true, cancellationToken))
        {
            throw new DomainException(CardPurchaseErrors.InvoicePaidConfirmationRequired);
        }

        var selection = await TagSelector.ResolveAsync(tags, command.TagIds, [], cancellationToken);
        var created = new List<CardPurchase>();
        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        if (plan is not null)
        {
            purchases.Add(plan);
        }

        foreach (var target in targets)
        {
            var invoice = await invoices.GetOrCreateAsync(card.Id, target.Month, cancellationToken);
            var purchase = plan is null
                ? CardPurchase.CreateManual(card.Id, invoice.Id, description, target.Amount, nature, date, command.Notes)
                : CardPurchase.CreateInstallment(card.Id, invoice.Id, plan, target.Number, target.Amount, command.Notes);
            purchase.ChangeSettlementTag(selection.SettlementTagId);
            purchases.Add(purchase);
            purchases.AddTags(purchase, selection.CommonTagIds);
            created.Add(purchase);
        }

        await purchases.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CardPurchaseCreateResult(await purchases.GetViewsAsync(created.Select(p => p.Id).ToList(), cancellationToken), warnings);
    }

    private InstallmentPlan BuildPlan(CreditCard card, string description, InstallmentsCommand installments, Money amount, DateOnly date)
    {
        var mode = CardPurchaseInput.ParseMode(installments.Mode);
        var total = installments.Total ?? throw new DomainException(CardPurchaseErrors.InstallmentsTotalRequired);
        if (total is < 1 or > InstallmentPlan.MaxInstallments)
        {
            throw new DomainException(CardPurchaseErrors.InstallmentsTotalInvalid);
        }

        var firstNumber = 1;
        YearMonth firstInvoice;
        if (mode == InstallmentMode.InProgress)
        {
            firstNumber = installments.NextNumber is { } next && next >= 1 && next <= total
                ? next
                : throw new DomainException(CardPurchaseErrors.InvalidNextInstallment);
            firstInvoice = string.IsNullOrEmpty(installments.FirstInvoiceMonth)
                ? InvoiceCycle.CurrentInvoice(clock.Today, card.ClosingDay, card.DueDay)
                : YearMonth.TryParse(installments.FirstInvoiceMonth, out var month)
                    ? month
                    : throw new DomainException(CardPurchaseErrors.InvalidFirstInvoiceMonth);
        }
        else
        {
            firstInvoice = InvoiceCycle.InvoiceFor(date, card.ClosingDay, card.DueDay);
        }

        return InstallmentPlan.Create(card.Id, description, mode, total, firstNumber, amount, date, firstInvoice);
    }
}

/// <summary>Obtém uma compra ou parcela.</summary>
public sealed class GetCardPurchaseHandler(ICardPurchaseStore purchases)
{
    public async Task<CardPurchaseView> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        (await purchases.GetViewsAsync([id], cancellationToken)).SingleOrDefault() ?? throw new DomainException(CardPurchaseErrors.NotFound);
}

/// <summary>Marca ou desmarca a compra como conferida (FR-027); não altera nenhum total.</summary>
public sealed class ReconcileCardPurchaseHandler(ICardPurchaseStore purchases)
{
    public async Task<CardPurchaseView> HandleAsync(Guid id, bool? reconciled, CancellationToken cancellationToken)
    {
        var purchase = await purchases.FindAsync(id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.NotFound);
        var value = reconciled ?? throw new DomainException(CardPurchaseErrors.ReconciledRequired);

        purchase.ChangeReconciled(value);
        await purchases.SaveAsync(cancellationToken);

        return (await purchases.GetViewsAsync([id], cancellationToken)).Single();
    }
}
