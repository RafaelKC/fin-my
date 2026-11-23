using FinMy.Domain.Common;

namespace FinMy.Application.Cards;

/// <summary>Erros estáveis de <c>/card-purchases</c> e <c>/installment-plans</c> (specs/003 contracts/openapi.yaml).</summary>
public static class CardPurchaseErrors
{
    public static readonly DomainError NotFound = new("not_found", "Compra não encontrada.", DomainErrorKind.NotFound);

    public static readonly DomainError PlanNotFound = new("not_found", "Parcelamento não encontrado.", DomainErrorKind.NotFound);

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Esta compra foi alterada em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    /// <summary>Compra que reabre uma fatura paga: o cliente repete com <c>confirmPaidInvoice</c> (research R5).</summary>
    public static readonly DomainError InvoicePaidConfirmationRequired = new(
        "card_purchase.invoice_paid_confirmation_required",
        "Esta fatura já foi paga e voltará a ter valor em aberto.",
        DomainErrorKind.Conflict);

    public static readonly DomainError PlanHasPaidInvoice = new(
        "installment_plan.has_paid_invoice",
        "Este parcelamento tem parcela em fatura paga e não pode ser recriado.",
        DomainErrorKind.Conflict);

    public static readonly DomainError CreditMustBeSingle = Invalid(
        "card_purchase.credit_must_be_single", "installments", "Estorno ou crédito não pode ser parcelado.");

    public static readonly DomainError InvalidNextInstallment = Invalid(
        "card_purchase.invalid_next_installment", "installments.nextNumber", "A próxima parcela deve ficar entre 1 e o total de parcelas.");

    public static readonly DomainError CardArchived = Invalid(
        "credit_card.archived", "creditCardId", "Este cartão está arquivado. Escolha outro cartão.");

    public static readonly DomainError CardRequired = Invalid("credit_card.required", "creditCardId", "Escolha o cartão.");

    public static readonly DomainError DateRequired = Invalid("date.required", "date", "Informe a data.");

    public static readonly DomainError VersionRequired = Invalid("validation_failed", "version", "Informe a versão da compra.");

    public static readonly DomainError ReconciledRequired = Invalid("validation_failed", "reconciled", "Informe se a compra está conferida.");

    public static readonly DomainError InvalidNature = Invalid("nature.invalid", "nature", "Escolha Débito ou Crédito.");

    public static readonly DomainError InvalidScope = Invalid("scope.invalid", "scope", "Escopo inválido para esta operação.");

    public static readonly DomainError InvalidDirection = Invalid("direction.invalid", "direction", "Use previous ou next.");

    public static readonly DomainError InvalidInstallmentMode = Invalid(
        "installment.mode_invalid", "installments.mode", "Escolha total_amount, installment_amount ou in_progress.");

    public static readonly DomainError InvalidAmountMode = Invalid(
        "installment.amount_mode_invalid", "amountMode", "Escolha total_amount ou installment_amount.");

    public static readonly DomainError InstallmentsTotalRequired = Invalid(
        "installment.total_invalid", "installments.total", "Informe o número de parcelas.");

    public static readonly DomainError InstallmentsTotalInvalid = Invalid(
        "installment.total_invalid", "installments.total", "O número de parcelas deve ficar entre 1 e 48.");

    public static readonly DomainError InvalidFirstInvoiceMonth = Invalid(
        "firstInvoiceMonth.invalid", "installments.firstInvoiceMonth", "Use o formato AAAA-MM.");

    public static readonly DomainError CarryOverNotEditable = Invalid(
        "card_purchase.carry_over_not_editable", "creditCardId", "O saldo levado para outra fatura não pode ser editado; exclua o par.");

    public static readonly DomainError InProgressNeedsInstallmentAmount = Invalid(
        "installment_plan.in_progress_needs_installment_amount", "amountMode", "Parcelamento em andamento só aceita o valor da parcela.");

    private static DomainError Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
