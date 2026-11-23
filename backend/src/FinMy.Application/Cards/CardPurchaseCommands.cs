using FinMy.Application.Abstractions;

namespace FinMy.Application.Cards;

/// <param name="Total">N (1 a 48).</param>
/// <param name="Mode"><c>total_amount</c>, <c>installment_amount</c> ou <c>in_progress</c>.</param>
/// <param name="NextNumber">Só <c>in_progress</c>: a próxima parcela p.</param>
/// <param name="FirstInvoiceMonth">Só <c>in_progress</c>: <c>AAAA-MM</c> da fatura da parcela p; ausente → fatura atual.</param>
public sealed record InstallmentsCommand(int? Total, string? Mode, int? NextNumber, string? FirstInvoiceMonth);

/// <param name="Amount">Expressão RT-05; negativa vira crédito (research R6).</param>
/// <param name="Installments">Ausente → à vista.</param>
/// <param name="ConfirmPaidInvoice">Confirma a compra numa fatura já paga (research R5).</param>
public sealed record CreateCardPurchaseCommand(
    string? Description,
    string? Amount,
    string? Nature,
    DateOnly? Date,
    Guid? CreditCardId,
    string? Notes,
    InstallmentsCommand? Installments,
    bool ConfirmPaidInvoice);

/// <param name="Scope"><c>this</c> (padrão) ou <c>all</c>: vale para descrição e observação das parcelas.</param>
/// <param name="Version">Obrigatória; divergente da atual → conflito de concorrência (research R11).</param>
public sealed record UpdateCardPurchaseCommand(
    Guid Id,
    string? Description,
    string? Amount,
    string? Nature,
    DateOnly? Date,
    Guid? CreditCardId,
    string? Notes,
    string? Scope,
    bool ConfirmPaidInvoice,
    long? Version);

/// <param name="Direction"><c>previous</c> ou <c>next</c>.</param>
/// <param name="Scope"><c>this</c> (padrão) ou <c>this_and_following</c>.</param>
public sealed record MoveCardPurchaseCommand(Guid Id, string? Direction, string? Scope);

/// <param name="AmountMode"><c>total_amount</c> ou <c>installment_amount</c>.</param>
public sealed record UpdateInstallmentPlanCommand(Guid Id, int? InstallmentTotal, string? Amount, string? AmountMode);

/// <summary>Resultado de <c>POST /card-purchases</c>: as compras criadas e os avisos (<c>recorded_as_credit</c>).</summary>
public sealed record CardPurchaseCreateResult(IReadOnlyList<CardPurchaseView> Purchases, IReadOnlyList<string> Warnings);
