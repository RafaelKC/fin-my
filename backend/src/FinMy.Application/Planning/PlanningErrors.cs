using FinMy.Domain.Common;

namespace FinMy.Application.Planning;

/// <summary>Erros estáveis de <c>/years/{yyyy}/planning</c> (007 contracts/api.md).</summary>
public static class PlanningErrors
{
    public const string CellNotEditableCode = "planning.cell_not_editable";

    /// <summary>409: a célula tem 2+ lançamentos ou algum efetivado; <c>items</c> traz a lista para edição individual (FR-003).</summary>
    public static DomainError CellNotEditable(IReadOnlyList<PlanningCellItem> items) => new(
        CellNotEditableCode,
        "Esta célula tem mais de um lançamento ou algum já efetivado. Edite os lançamentos individualmente.",
        DomainErrorKind.Conflict,
        Extensions: new Dictionary<string, object?> { ["items"] = items });

    public static DomainError Field(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
