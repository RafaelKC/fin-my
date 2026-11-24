using FinMy.Application.Recurrences;

namespace FinMy.Api.Endpoints.Recurrences;

/// <summary>Corpo de <c>POST /recurrences</c> (<c>RecurrenceInput</c>); <c>amount</c> é texto (RT-05) e os meses são <c>AAAA-MM</c>.</summary>
public sealed record RecurrenceRequest(
    string? Kind,
    string? Description,
    string? Amount,
    int? DayOfMonth,
    Guid? WalletId,
    Guid? CreditCardId,
    IReadOnlyList<Guid>? TagIds,
    string? StartMonth,
    string? EndType,
    string? EndMonth,
    int? OccurrenceLimit,
    string? ReferenceMonthRule)
{
    public CreateRecurrenceCommand ToCreateCommand() => new(
        Kind, Description, Amount, DayOfMonth, WalletId, CreditCardId, TagIds, StartMonth, EndType, EndMonth, OccurrenceLimit, ReferenceMonthRule);
}

/// <summary>Recorrência na resposta (<c>Recurrence</c>); dinheiro em centavos.</summary>
public sealed record RecurrenceResponse(
    Guid Id,
    string Kind,
    string Description,
    long AmountCents,
    int DayOfMonth,
    Guid? WalletId,
    Guid? CreditCardId,
    IReadOnlyList<Guid> TagIds,
    string StartMonth,
    string EndType,
    string? EndMonth,
    int? OccurrenceLimit,
    string? ReferenceMonthRule,
    string? GeneratedUntilMonth,
    string Status,
    DateOnly? NextOccurrenceDate,
    bool NeedsReview,
    string? ReviewReason,
    long Version)
{
    public static RecurrenceResponse From(RecurrenceView r) => new(
        r.Id,
        RecurrenceInput.ToText(r.Kind),
        r.Description,
        r.AmountCents,
        r.DayOfMonth,
        r.WalletId,
        r.CreditCardId,
        r.TagIds,
        r.StartMonth.ToString(),
        RecurrenceInput.ToText(r.EndType),
        r.EndMonth?.ToString(),
        r.OccurrenceLimit,
        r.ReferenceMonthRule is { } rule ? RecurrenceInput.ToText(rule) : null,
        r.GeneratedUntilMonth?.ToString(),
        r.Status,
        r.NextOccurrenceDate,
        r.NeedsReview,
        r.ReviewReason,
        r.Version);
}

/// <summary>Resposta de <c>GET /recurrences</c>: itens e o total mensal previsto das ativas.</summary>
public sealed record RecurrenceListResponse(IReadOnlyList<RecurrenceResponse> Items, long MonthlyIncomeCents, long MonthlyExpenseCents);
