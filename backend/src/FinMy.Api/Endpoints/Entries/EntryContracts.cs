using FinMy.Application.Abstractions;
using FinMy.Application.Entries;

namespace FinMy.Api.Endpoints.Entries;

/// <summary>Corpo de <c>POST /entries</c> (<c>EntryCreate</c>). <c>kind</c>/<c>status</c> em texto: <c>income|expense</c>, <c>planned|settled</c>.</summary>
public sealed record EntryCreateRequest(
    string? Kind,
    string? Description,
    string? Amount,
    DateOnly? Date,
    Guid? WalletId,
    string? Status,
    string? ReferenceMonth,
    string? Notes);

/// <summary>Corpo de <c>PUT /entries/{id}</c> (<c>EntryUpdate</c>).</summary>
public sealed record EntryUpdateRequest(
    string? Kind,
    string? Description,
    string? Amount,
    DateOnly? Date,
    Guid? WalletId,
    string? Status,
    string? ReferenceMonth,
    string? Notes,
    long? Version);

/// <summary>Corpo de <c>PATCH /entries/{id}/status</c>.</summary>
public sealed record EntryStatusRequest(string? Status);

/// <summary>Corpo de <c>POST /entries/bulk</c>: <c>action</c> é <c>settle|move_to_current_month|delete</c>.</summary>
public sealed record EntryBulkRequest(IReadOnlyList<Guid>? Ids, string? Action);

/// <summary>Resposta de <c>POST /entries/bulk</c>; <c>deletionBatchId</c> só vem na ação <c>delete</c>.</summary>
public sealed record EntryBulkResponse(int Affected, Guid? DeletionBatchId);

/// <summary>Entrada/Saída na resposta (<c>Entry</c>); dinheiro em centavos.</summary>
public sealed record EntryResponse(
    Guid Id,
    string Kind,
    string Description,
    long AmountCents,
    DateOnly Date,
    string ReferenceMonth,
    string Status,
    Guid WalletId,
    string? Notes,
    DateTimeOffset CreatedAt,
    long Version)
{
    public static EntryResponse From(EntryView e) => new(
        e.Id,
        EntryInput.ToText(e.Kind),
        e.Description,
        e.AmountCents,
        e.Date,
        e.ReferenceMonth.ToString(),
        EntryInput.ToText(e.Status),
        e.WalletId,
        e.Notes,
        e.CreatedAt,
        e.Version);
}

/// <summary>Item de <c>GET /entries/suggestions</c>.</summary>
public sealed record EntrySuggestionResponse(string Description, int Uses, Guid? WalletId);
