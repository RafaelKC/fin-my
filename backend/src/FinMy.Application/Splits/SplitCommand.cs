using FinMy.Application.Abstractions;
using FinMy.Application.Entries;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Splits;

namespace FinMy.Application.Splits;

/// <param name="SettlementTagId">Nulo = "Eu (sem tag de acerto)".</param>
/// <param name="Amount">Expressão de valor (RT-05); obrigatória só em <c>amounts</c>.</param>
public sealed record SplitPartCommand(Guid? SettlementTagId, string? Amount);

/// <param name="Mode"><c>equal</c> ou <c>amounts</c>.</param>
public sealed record SplitCommand(string? Mode, IReadOnlyList<SplitPartCommand>? Parts);

/// <summary>Valida o pedido de divisão e calcula as partes (004 research R5), igual para Saídas e compras.</summary>
internal static class SplitResolver
{
    public static async Task<IReadOnlyList<SplitPart>> PlanAsync(
        ITagStore tags, long totalCents, SplitCommand? split, CancellationToken cancellationToken)
    {
        var command = split ?? throw new DomainException(SplitErrors.Required);
        var mode = command.Mode switch
        {
            "equal" => SplitMode.Equal,
            "amounts" => SplitMode.Amounts,
            _ => throw new DomainException(SplitErrors.InvalidMode),
        };

        var requests = (command.Parts ?? [])
            .Select(p => new SplitPartRequest(
                p.SettlementTagId,
                mode == SplitMode.Amounts ? EntryInput.ParseAmount(p.Amount).Cents : null))
            .ToList();

        var parts = SplitPlanner.Plan(totalCents, mode, requests);

        var tagIds = parts.Where(p => p.SettlementTagId is not null).Select(p => p.SettlementTagId!.Value).ToList();
        if (tagIds.Count > 0)
        {
            var found = await tags.FindManyAsync(tagIds, cancellationToken);
            if (found.Count != tagIds.Count)
            {
                throw new DomainException(TagErrors.NotFound);
            }

            if (found.Any(t => t.IsArchived))
            {
                throw new DomainException(TagErrors.ArchivedTag);
            }

            if (found.Any(t => !t.IsSettlement))
            {
                throw new DomainException(SplitErrors.PartNotSettlement);
            }
        }

        return parts;
    }

    /// <summary>Tags comuns do corpo; tag de acerto aqui é recusada, pois cada parte define a sua.</summary>
    public static async Task<IReadOnlyList<Guid>> CommonTagsAsync(ITagStore tags, IReadOnlyList<Guid>? requested, CancellationToken cancellationToken)
    {
        var selection = await TagSelector.ResolveAsync(tags, requested, [], cancellationToken);
        return selection.SettlementTagId is null ? selection.CommonTagIds : throw new DomainException(SplitErrors.SettlementTagInTagIds);
    }
}
