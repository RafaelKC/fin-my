using FinMy.Domain.Entries;

namespace FinMy.Application.Planning;

/// <summary>Lançamento de uma célula não editável (extensão <c>items</c> do 409).</summary>
public sealed record PlanningCellItem(Guid Id, string Description, long AmountCents, DateOnly Date, string Status, Guid WalletId);

/// <param name="Kind"><c>income</c>, <c>expense</c> ou <c>invoice</c>.</param>
/// <param name="Description">Entrada/Saída: descrição da linha (a chave é a descrição normalizada).</param>
/// <param name="CreditCardId">Fatura: o cartão.</param>
/// <param name="Month"><c>AAAA-MM</c> da coluna (de vencimento, na fatura).</param>
/// <param name="Value">Expressão de valor (RT-05, <c>=</c> inicial aceito); vazio apaga.</param>
/// <param name="WalletId">Só de "Nova linha": carteira de um lançamento novo (FR-005).</param>
/// <param name="Day">Só de "Nova linha": dia de um lançamento novo (FR-004).</param>
public sealed record SetPlanningCellCommand(
    string? Year, string? Kind, string? Description, Guid? CreditCardId, string? Month, string? Value, Guid? WalletId, int? Day);

/// <param name="Months"><c>AAAA-MM</c> dos meses a preencher (do ano da rota).</param>
public sealed record FillPlanningMonthsCommand(
    string? Year, string? Kind, string? Description, Guid? CreditCardId, string? Amount, IReadOnlyList<string>? Months, Guid? WalletId, int? Day);

/// <summary>Resposta de uma edição: a linha atualizada (de Entrada/Saída ou de fatura) e os totais dos 12 meses.</summary>
public sealed record PlanningRowOutcome(PlanningRow? Row, InvoiceRow? InvoiceRow, IReadOnlyList<PlanningTotalsMonth> Totals);

public sealed record SetPlanningCellResult(PlanningRowOutcome Outcome, Guid? DeletionBatchId);

public sealed record FillPlanningMonthsResult(int Created, int Skipped, PlanningRowOutcome Outcome);

internal static class PlanningKinds
{
    public const string Invoice = "invoice";

    public static string Text(EntryKind kind) => kind == EntryKind.Income ? "income" : "expense";
}
