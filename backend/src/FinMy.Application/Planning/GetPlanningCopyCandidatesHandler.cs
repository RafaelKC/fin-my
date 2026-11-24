using FinMy.Application.Abstractions;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>
/// <c>GET /years/{yyyy}/planning/copy-candidates</c> (007 US3.1, US3.3, research R4): linhas do ano anterior sem ocorrências de
/// recorrência, com os valores por mês, e as estimativas de fatura do ano anterior. Só lê; nada é criado.
/// </summary>
public sealed class GetPlanningCopyCandidatesHandler(IPlanningReader reader, ITagStore tags, IClock clock)
{
    public async Task<CopyCandidatesResult> HandleAsync(string? yearText, CancellationToken cancellationToken)
    {
        var year = PlanningCopySupport.ParseYear(yearText);
        var previous = await reader.ReadAsync(year - 1, clock.Today, cancellationToken);

        var groups = PlanningCopySupport.Copyable(previous)
            .GroupBy(e => (e.Kind, e.DescriptionNormalized, e.SettlementTagId))
            .OrderBy(g => g.Key.Kind)
            .ThenBy(g => g.Key.DescriptionNormalized, StringComparer.Ordinal)
            .ThenBy(g => g.Key.SettlementTagId)
            .ToList();

        var tagIds = groups.Select(g => g.Key.SettlementTagId).OfType<Guid>().Distinct().ToList();
        var names = tagIds.Count == 0
            ? []
            : (await tags.FindManyAsync(tagIds, cancellationToken)).ToDictionary(t => t.Id, t => t.Name);

        var rows = groups
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).First();
                return new CopyCandidateRow(
                    PlanningCopySupport.Key(latest),
                    g.Key.Kind,
                    latest.Description,
                    g.Key.SettlementTagId,
                    g.Key.SettlementTagId is { } tag ? names.GetValueOrDefault(tag) : null,
                    Enumerable.Range(1, 12)
                        .Select(m => YearMonth.Of(year - 1, m))
                        .Select(month => g.Where(e => e.Month == month).ToList())
                        .Select((items, i) => new CopyCandidateCell(YearMonth.Of(year - 1, i + 1), items.Sum(e => e.AmountCents), items.Count))
                        .ToList());
            })
            .ToList();

        return new CopyCandidatesResult(rows, EstimateCards(previous));
    }

    internal static IReadOnlyList<CopyEstimateCard> EstimateCards(PlanningData previous) =>
        previous.Cards
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id)
            .Select(c => new CopyEstimateCard(
                c.Id,
                c.Name,
                previous.Invoices
                    .Where(i => i.CreditCardId == c.Id && i.EstimateCents is not null)
                    .OrderBy(i => i.DueMonth)
                    .Select(i => new CopyEstimateMonth(i.DueMonth, i.EstimateCents!.Value))
                    .ToList()))
            .Where(c => c.Months.Count > 0)
            .ToList();
}
