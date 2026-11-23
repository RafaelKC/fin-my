using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Tags;

/// <summary>Erros estáveis de <c>/tags</c> (contracts/openapi.yaml).</summary>
public static class TagErrors
{
    public static readonly DomainError NotFound = new("not_found", "Tag não encontrada.", DomainErrorKind.NotFound);

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Esta tag foi alterada em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    public static readonly DomainError InUseArchiveInstead = new(
        "tag.in_use_archive_instead", "Esta tag está em uso. Você pode arquivá-la.", DomainErrorKind.Conflict);

    public static readonly DomainError MergeIntoSelf = Invalid(
        "tag.merge_into_self", "targetTagId", "Escolha outra tag para juntar.");

    public static readonly DomainError InvalidArchivedFilter = Invalid(
        "validation_failed", "archived", "Use false, true ou all.");

    public static readonly DomainError VersionRequired = Invalid(
        "validation_failed", "version", "Informe a versão da tag.");

    public static DomainError NameTaken(Guid? existingTagId) => new(
        "tag.name_taken",
        "Já existe uma tag com esse nome.",
        DomainErrorKind.Conflict,
        new Dictionary<string, string[]> { ["name"] = ["Já existe uma tag com esse nome."] },
        Extensions: existingTagId is { } id ? new Dictionary<string, object?> { ["existingTagId"] = id } : null);

    public static DomainError SettlementConflict(IReadOnlyList<TagConflict> conflicts) => new(
        "tag.settlement_conflict",
        "Alguns lançamentos ficariam com duas tags de acerto. Corrija-os antes.",
        DomainErrorKind.Conflict,
        Extensions: new Dictionary<string, object?>
        {
            ["conflicts"] = conflicts.Select(c => new { type = c.Type, id = c.Id, description = c.Description, date = c.Date }).ToList(),
        });

    private static DomainError Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
