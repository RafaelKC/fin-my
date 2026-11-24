using FinMy.Application.Abstractions;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <summary>Recorrência como o cliente a vê (contrato <c>Recurrence</c>); <see cref="Version"/> é o <c>xmin</c>.</summary>
public sealed record RecurrenceView(
    Guid Id,
    RecurrenceKind Kind,
    string Description,
    long AmountCents,
    int DayOfMonth,
    Guid? WalletId,
    Guid? CreditCardId,
    IReadOnlyList<Guid> TagIds,
    YearMonth StartMonth,
    RecurrenceEndType EndType,
    YearMonth? EndMonth,
    int? OccurrenceLimit,
    ReferenceMonthRule? ReferenceMonthRule,
    YearMonth? GeneratedUntilMonth,
    string Status,
    DateOnly? NextOccurrenceDate,
    bool NeedsReview,
    string? ReviewReason,
    long Version);

/// <summary>Lista de recorrências com o total mensal previsto das ativas (005 FR-009).</summary>
public sealed record RecurrenceListView(IReadOnlyList<RecurrenceView> Items, long MonthlyIncomeCents, long MonthlyExpenseCents);

/// <summary>Monta os <see cref="RecurrenceView"/>: situação, próxima ocorrência e aviso de revisão (research R2, R6).</summary>
public sealed class RecurrenceViewBuilder(IRecurrenceStore store, RecurrenceReviewer reviewer, IClock clock)
{
    public const string Active = "active";
    public const string Ended = "ended";

    /// <summary>Encerrada quando o mês final já passou (o mês de hoje ainda conta como ativo).</summary>
    public static string StatusOf(Recurrence recurrence, DateOnly today) =>
        recurrence.EndYearMonth is { } end && end < YearMonth.From(today) ? Ended : Active;

    public async Task<IReadOnlyList<RecurrenceView>> BuildAsync(IReadOnlyList<Recurrence> recurrences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recurrences);

        var today = clock.Today;
        var tags = await store.GetTagIdsAsync(recurrences, cancellationToken);
        var next = await store.GetNextOccurrenceDatesAsync(recurrences.Select(r => r.Id).ToList(), today, cancellationToken);

        var views = new List<RecurrenceView>(recurrences.Count);
        foreach (var r in recurrences)
        {
            var tagIds = tags.GetValueOrDefault(r.Id) ?? [];
            var reason = await reviewer.ReasonAsync(r, tagIds, cancellationToken);
            views.Add(new RecurrenceView(
                r.Id,
                r.Kind,
                r.Description,
                r.AmountCents,
                r.DayOfMonth,
                r.WalletId,
                r.CreditCardId,
                tagIds,
                r.StartYearMonth,
                r.EndType,
                r.EndYearMonth,
                r.OccurrenceLimit,
                r.ReferenceMonthRule,
                r.GeneratedUntil,
                StatusOf(r, today),
                next.TryGetValue(r.Id, out var date) ? date : null,
                reason is not null,
                reason,
                store.VersionOf(r)));
        }

        return views;
    }
}
