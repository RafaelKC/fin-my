using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>
/// Compra ou parcela no cartão (<c>card_purchases</c>, 003 FR-008..016). Valor sempre positivo; o sentido vem de
/// <see cref="Nature"/>. Compra e fatura são sempre do mesmo cartão (FK tripla no banco). Crédito (estorno) é sempre à vista.
/// </summary>
public sealed class CardPurchase : TenantEntity
{
    // Construtor para o EF Core.
    private CardPurchase()
    {
    }

    private CardPurchase(
        Guid creditCardId,
        Guid invoiceId,
        string description,
        Money amount,
        PurchaseNature nature,
        DateOnly purchaseDate,
        PurchaseOrigin origin,
        string? notes)
    {
        CreditCardId = creditCardId;
        InvoiceId = invoiceId;
        Nature = nature;
        Origin = origin;
        ChangeDescription(description);
        ChangeAmount(amount);
        ChangeNotes(notes);
        PurchaseDate = Rules.Date(purchaseDate, "purchaseDate");
    }

    public Guid CreditCardId { get; private set; }

    public Guid InvoiceId { get; private set; }

    /// <summary>Descrição base; a exibição acrescenta "k/N" quando é parcela (<see cref="DisplayDescription"/>).</summary>
    public string Description { get; private set; } = string.Empty;

    public string DescriptionNormalized { get; private set; } = string.Empty;

    public long AmountCents { get; private set; }

    public PurchaseNature Nature { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    public PurchaseOrigin Origin { get; private set; }

    /// <summary>"Conferida" contra a fatura do banco (003 FR-027).</summary>
    public bool Reconciled { get; private set; }

    /// <summary>Compra movida à mão para outra fatura: a mudança de dias do cartão não a redistribui (003 FR-028).</summary>
    public bool ManuallyMoved { get; private set; }

    public string? Notes { get; private set; }

    public Guid? InstallmentPlanId { get; private set; }

    public int? InstallmentNumber { get; private set; }

    public int? InstallmentTotal { get; private set; }

    /// <summary>Mesmo valor nas duas compras do par de "saldo levado" (003 FR-020).</summary>
    public Guid? CarryOverPairId { get; private set; }

    public Money Amount => new(AmountCents);

    /// <summary>Descrição exibida: base + " k/N" quando é parcela.</summary>
    public string DisplayDescription =>
        InstallmentNumber is { } k && InstallmentTotal is { } n ? $"{Description} {k}/{n}" : Description;

    /// <summary>Compra à vista lançada à mão (débito ou crédito/estorno).</summary>
    public static CardPurchase CreateManual(
        Guid creditCardId,
        Guid invoiceId,
        string description,
        Money amount,
        PurchaseNature nature,
        DateOnly purchaseDate,
        string? notes) =>
        new(creditCardId, invoiceId, description, amount, nature, purchaseDate, PurchaseOrigin.Manual, notes);

    /// <summary>Parcela <paramref name="number"/> de <paramref name="total"/> de um plano; sempre débito.</summary>
    public static CardPurchase CreateInstallment(
        Guid creditCardId,
        Guid invoiceId,
        InstallmentPlan plan,
        int number,
        Money amount,
        string? notes)
    {
        if (number < 1 || number > plan.InstallmentTotal)
        {
            throw Rules.Invalid("installment.number_invalid", "installmentNumber", "O número da parcela deve ficar entre 1 e o total de parcelas");
        }

        return new CardPurchase(
            creditCardId, invoiceId, plan.Description, amount, PurchaseNature.Debit, plan.PurchaseDate, PurchaseOrigin.Installment, notes)
        {
            InstallmentPlanId = plan.Id,
            InstallmentNumber = number,
            InstallmentTotal = plan.InstallmentTotal,
        };
    }

    /// <summary>Uma das duas compras do par de "saldo levado" (débito na fatura nova, crédito na antiga — FR-020).</summary>
    public static CardPurchase CreateCarryOver(
        Guid creditCardId,
        Guid invoiceId,
        string description,
        Money amount,
        PurchaseNature nature,
        DateOnly purchaseDate,
        Guid pairId) =>
        new(creditCardId, invoiceId, description, amount, nature, purchaseDate, PurchaseOrigin.CarryOver, null)
        {
            CarryOverPairId = pairId,
        };

    public void ChangeDescription(string description)
    {
        Description = Rules.RequiredText(description, Rules.DescriptionMaxLength, "description", "description", "a descrição");
        DescriptionNormalized = TextNormalizer.Normalize(Description);
    }

    public void ChangeAmount(Money amount) => AmountCents = Rules.EntryAmount(amount);

    /// <summary>Débito ou crédito. Parcela é sempre débito: estorno é à vista (003 FR-009, research R6).</summary>
    public void ChangeNature(PurchaseNature nature)
    {
        if (nature == PurchaseNature.Credit && InstallmentPlanId is not null)
        {
            throw Rules.Invalid("card_purchase.credit_must_be_single", "nature", "Estorno ou crédito não pode ser parcelado");
        }

        Nature = nature;
    }

    public void ChangeDate(DateOnly purchaseDate) => PurchaseDate = Rules.Date(purchaseDate, "purchaseDate");

    /// <summary>
    /// Passa a compra para outro cartão e fatura (FR-015). A FK tripla garante que a fatura é do cartão; a marca de
    /// "movida à mão" não muda.
    /// </summary>
    public void Reassign(Guid creditCardId, Guid invoiceId)
    {
        CreditCardId = creditCardId;
        InvoiceId = invoiceId;
    }

    public void ChangeNotes(string? notes) =>
        Notes = Rules.OptionalText(notes, Rules.NotesMaxLength, "notes", "notes", "a observação");

    public void ChangeReconciled(bool reconciled) => Reconciled = reconciled;

    /// <summary>Move para outra fatura do mesmo cartão e marca como movida à mão (FR-028).</summary>
    public void MoveToInvoice(Guid invoiceId)
    {
        InvoiceId = invoiceId;
        ManuallyMoved = true;
    }

    /// <summary>Redistribuição automática pela mudança de dias do cartão (research R3): não marca como movida à mão.</summary>
    public void Redistribute(Guid invoiceId) => InvoiceId = invoiceId;
}
