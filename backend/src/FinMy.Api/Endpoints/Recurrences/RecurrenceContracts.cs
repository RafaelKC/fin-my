using System.Text.Json.Serialization;
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

/// <summary>
/// Corpo de <c>PUT /recurrences/{id}</c>: o modelo completo + <c>fromMonth</c> (<c>AAAA-MM</c>, padrão o mês de hoje) + <c>version</c>.
/// O tipo é conferido (não muda) e o mês inicial não é lido: ele nunca muda.
/// </summary>
public sealed record RecurrenceUpdateRequest(
    string? Kind,
    string? Description,
    string? Amount,
    int? DayOfMonth,
    Guid? WalletId,
    Guid? CreditCardId,
    IReadOnlyList<Guid>? TagIds,
    string? EndType,
    string? EndMonth,
    int? OccurrenceLimit,
    string? ReferenceMonthRule,
    string? FromMonth,
    long? Version)
{
    public UpdateRecurrenceCommand ToCommand(Guid id) => new(
        id, Kind, Description, Amount, DayOfMonth, WalletId, CreditCardId, TagIds, EndType, EndMonth, OccurrenceLimit, ReferenceMonthRule, FromMonth, Version);
}

/// <summary>Corpo de <c>POST /recurrences/from-item</c>: o lançamento a repetir (<c>type</c> <c>entry</c> ou <c>card_purchase</c>) e o término.</summary>
public sealed record RecurrenceFromItemRequest(string? Type, Guid? Id, string? EndType, string? EndMonth, int? OccurrenceLimit)
{
    public CreateRecurrenceFromItemCommand ToCommand() => new(Type, Id, EndType, EndMonth, OccurrenceLimit);
}

/// <summary>Corpo de <c>POST /recurrences/{id}/end</c>.</summary>
public sealed record RecurrenceEndRequest(string? FromMonth);

/// <summary>Resposta de <c>POST /recurrences/{id}/end</c>.</summary>
public sealed record RecurrenceEndResponse(RecurrenceResponse Recurrence, int DeletedCount, Guid DeletionBatchId);

/// <summary>Resposta de <c>DELETE /recurrences/{id}</c>.</summary>
public sealed record RecurrenceDeleteResponse(int DeletedCount, int UnlinkedCount);

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
    /// <summary>Quantas ocorrências a alteração alcançou; só vem na resposta de <c>PUT /recurrences/{id}</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AffectedCount { get; init; }

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
