using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>
/// Grupo de parcelamento (<c>installment_plans</c>, 003 FR-012..016): guarda como a compra foi informada. As parcelas
/// são <see cref="CardPurchase"/> ligadas a este plano. No modo <see cref="InstallmentMode.TotalAmount"/> vale o valor
/// total; nos demais, o valor de cada parcela.
/// </summary>
public sealed class InstallmentPlan : TenantEntity
{
    public const int MaxInstallments = 48;

    // Construtor para o EF Core.
    private InstallmentPlan()
    {
    }

    private InstallmentPlan(
        Guid creditCardId,
        string description,
        InstallmentMode mode,
        int installmentTotal,
        int firstNumber,
        Money amount,
        DateOnly purchaseDate,
        YearMonth firstInvoiceDueMonth)
    {
        CreditCardId = creditCardId;
        ChangeDescription(description);
        Configure(mode, installmentTotal, firstNumber, amount, firstInvoiceDueMonth);
        PurchaseDate = Rules.Date(purchaseDate, "purchaseDate");
    }

    public Guid CreditCardId { get; private set; }

    /// <summary>Descrição base, sem o "k/N".</summary>
    public string Description { get; private set; } = string.Empty;

    public InstallmentMode Mode { get; private set; }

    /// <summary>Total de parcelas (N), de 1 a 48.</summary>
    public int InstallmentTotal { get; private set; }

    /// <summary>Número da primeira parcela gerada: 1, ou p no modo em andamento.</summary>
    public int FirstNumber { get; private set; }

    public long? TotalAmountCents { get; private set; }

    public long? InstallmentAmountCents { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    /// <summary>Fatura (mês de vencimento, dia 1) da parcela <see cref="FirstNumber"/>.</summary>
    public DateOnly FirstInvoiceDueMonth { get; private set; }

    public void ChangeDescription(string description) =>
        Description = Rules.RequiredText(description, Rules.DescriptionMaxLength, "description", "description", "a descrição");

    /// <summary>Cartão e data valem para todas as parcelas (FR-015); <paramref name="firstInvoiceDueMonth"/> acompanha a nova fatura da primeira.</summary>
    public void Relocate(Guid creditCardId, DateOnly purchaseDate, YearMonth firstInvoiceDueMonth)
    {
        CreditCardId = creditCardId;
        PurchaseDate = Rules.Date(purchaseDate, "purchaseDate");
        FirstInvoiceDueMonth = Rules.Date(firstInvoiceDueMonth.FirstDay, "firstInvoiceDueMonth");
    }

    /// <summary>Altera N, o valor ou o modo do valor (FR-015); quem chama recria as parcelas.</summary>
    public void Reconfigure(InstallmentMode mode, int installmentTotal, Money amount) =>
        Configure(mode, installmentTotal, FirstNumber, amount, YearMonth.From(FirstInvoiceDueMonth));

    private void Configure(InstallmentMode mode, int installmentTotal, int firstNumber, Money amount, YearMonth firstInvoiceDueMonth)
    {
        if (installmentTotal is < 1 or > MaxInstallments)
        {
            throw Rules.Invalid("installment.total_invalid", "installments", $"O número de parcelas deve ficar entre 1 e {MaxInstallments}");
        }

        var first = mode == InstallmentMode.InProgress ? firstNumber : 1;
        if (first < 1 || first > installmentTotal)
        {
            throw Rules.Invalid("installment.first_number_invalid", "firstNumber", "A próxima parcela deve ficar entre 1 e o total de parcelas");
        }

        var cents = Rules.EntryAmount(amount);
        var firstMonth = Rules.Date(firstInvoiceDueMonth.FirstDay, "firstInvoiceDueMonth");

        Mode = mode;
        InstallmentTotal = installmentTotal;
        FirstNumber = first;
        TotalAmountCents = mode == InstallmentMode.TotalAmount ? cents : null;
        InstallmentAmountCents = mode == InstallmentMode.TotalAmount ? null : cents;
        FirstInvoiceDueMonth = firstMonth;
    }

    /// <summary>
    /// Cria o plano. <paramref name="amount"/> é o total no modo <see cref="InstallmentMode.TotalAmount"/> e o valor da
    /// parcela nos demais; <paramref name="firstNumber"/> só conta no modo em andamento.
    /// </summary>
    public static InstallmentPlan Create(
        Guid creditCardId,
        string description,
        InstallmentMode mode,
        int installmentTotal,
        int firstNumber,
        Money amount,
        DateOnly purchaseDate,
        YearMonth firstInvoiceDueMonth) =>
        new(creditCardId, description, mode, installmentTotal, firstNumber, amount, purchaseDate, firstInvoiceDueMonth);
}
