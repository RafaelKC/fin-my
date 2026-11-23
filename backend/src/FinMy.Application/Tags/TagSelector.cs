using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Tags;

/// <summary>Tags de um lançamento já separadas (research R1): a de acerto vai para a coluna, as demais para as ligações.</summary>
/// <param name="SettlementTagId">A única Tag de acerto, se houver.</param>
/// <param name="CommonTagIds">Tags comuns, sem repetição.</param>
public sealed record TagSelection(Guid? SettlementTagId, IReadOnlyList<Guid> CommonTagIds)
{
    public static TagSelection None { get; } = new(null, []);
}

/// <summary>
/// Valida e separa o <c>tagIds</c> que chega nos lançamentos (004 FR-002, research R1): até 20 tags, no máximo uma de acerto,
/// tag de outro Espaço → 404, tag arquivada só pode ser mantida (já estava no lançamento), nunca adicionada.
/// </summary>
public static class TagSelector
{
    public const int MaxTags = 20;

    /// <param name="requested"><c>tagIds</c> do corpo; <c>null</c> ou vazio = nenhuma tag.</param>
    /// <param name="current">Tags que o lançamento já tem (na criação, nenhuma): só estas podem estar arquivadas.</param>
    public static async Task<TagSelection> ResolveAsync(
        ITagStore tags, IReadOnlyCollection<Guid>? requested, IReadOnlyCollection<Guid> current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(current);

        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count > MaxTags)
        {
            throw new DomainException(TagErrors.TooManyTags);
        }

        if (ids.Count == 0)
        {
            return TagSelection.None;
        }

        var found = await tags.FindManyAsync(ids, cancellationToken);
        if (found.Count != ids.Count)
        {
            throw new DomainException(TagErrors.NotFound);
        }

        if (found.Any(t => t.IsArchived && !current.Contains(t.Id)))
        {
            throw new DomainException(TagErrors.ArchivedTag);
        }

        var settlement = found.Where(t => t.IsSettlement).ToList();
        if (settlement.Count > 1)
        {
            throw new DomainException(TagErrors.MultipleSettlement);
        }

        return new TagSelection(
            settlement.SingleOrDefault()?.Id,
            ids.Where(id => settlement.All(s => s.Id != id)).ToList());
    }

    /// <summary>Lê o filtro <c>tagIds=</c> (uuids separados por vírgula); vazio = sem filtro.</summary>
    public static IReadOnlyList<Guid> ParseFilter(string? text)
    {
        var ids = new List<Guid>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                throw new DomainException(TagErrors.InvalidTagFilter);
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids.Count <= MaxTags ? ids : throw new DomainException(TagErrors.InvalidTagFilter);
    }
}
