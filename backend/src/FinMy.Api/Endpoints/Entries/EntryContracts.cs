using FinMy.Api.Endpoints.Cards;
using FinMy.Api.Endpoints.Transfers;
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
    string? Notes,
    IReadOnlyList<Guid>? TagIds = null);

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
    long? Version,
    IReadOnlyList<Guid>? TagIds = null);

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
    string CreatedVia,
    long Version,
    IReadOnlyList<Guid> TagIds,
    Guid? SettlementTagId,
    Guid? SplitGroupId,
    int SplitCount)
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
        ChannelText.Of(e.CreatedVia),
        e.Version,
        e.TagIds,
        e.SettlementTagId,
        e.SplitGroupId,
        e.SplitCount);
}

/// <summary>Item de <c>GET /entries/suggestions</c>.</summary>
public sealed record EntrySuggestionResponse(string Description, int Uses, Guid? WalletId, Guid? CreditCardId, IReadOnlyList<Guid> TagIds);

/// <summary>Corpo de <c>POST /entries/{id}/duplicate</c>: só a nova data.</summary>
public sealed record EntryDuplicateRequest(DateOnly? Date);

/// <summary>Item de <c>GET /entries/search</c> (<c>LedgerItem</c>): <c>type</c> diz qual dos três objetos veio preenchido.</summary>
public sealed record LedgerItemResponse(string Type, EntryResponse? Entry, TransferResponse? Transfer, CardPurchaseResponse? Purchase)
{
    public static LedgerItemResponse From(LedgerItemView item) => new(
        item.Type switch
        {
            LedgerItemKind.Income => "income",
            LedgerItemKind.Expense => "expense",
            LedgerItemKind.CardPurchase => "card_purchase",
            _ => "transfer",
        },
        item.Entry is null ? null : EntryResponse.From(item.Entry),
        item.Transfer is null ? null : TransferResponse.From(item.Transfer),
        item.Purchase is null ? null : CardPurchaseResponse.From(item.Purchase));
}

/// <summary>Resposta de <c>GET /entries/search</c>; os totais valem para todos os resultados, não só a página.</summary>
public sealed record LedgerSearchResponse(IReadOnlyList<LedgerItemResponse> Items, long TotalCents, int TotalCount, string? NextCursor);
