using FinMy.Api.Idempotency;
using FinMy.Application.Entries;

namespace FinMy.Api.Endpoints.Entries;

/// <summary>
/// Entradas e Saídas (US2, US6.1, US6.2): <c>POST /entries</c>, <c>GET|PUT /entries/{id}</c>,
/// <c>PATCH /entries/{id}/status</c> e <c>GET /entries/suggestions</c> (contracts/openapi.yaml).
/// </summary>
public sealed class EntryEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/entries").WithTags("entries").RequireAuthorization();

        group.MapPost("/", async (EntryCreateRequest body, CreateEntryHandler handler, CancellationToken cancellationToken) =>
        {
            var entry = await handler.HandleAsync(
                new CreateEntryCommand(body.Kind, body.Description, body.Amount, body.Date, body.WalletId, body.Status, body.ReferenceMonth, body.Notes),
                cancellationToken);
            return Results.Created($"/api/v1/entries/{entry.Id}", EntryResponse.From(entry));
        }).WithIdempotency();

        // Antes de "/{id}" só por clareza: o literal já vence o parâmetro, e o id exige GUID.
        group.MapGet("/suggestions", async (string? q, string? kind, SuggestEntriesHandler handler, CancellationToken cancellationToken) =>
        {
            var suggestions = await handler.HandleAsync(q, kind, cancellationToken);
            return Results.Ok(suggestions.Select(s => new EntrySuggestionResponse(s.Description, s.Uses, s.WalletId)));
        });

        group.MapGet("/{id:guid}", async (Guid id, GetEntryHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(EntryResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPut("/{id:guid}", async (Guid id, EntryUpdateRequest body, UpdateEntryHandler handler, CancellationToken cancellationToken) =>
        {
            var entry = await handler.HandleAsync(
                new UpdateEntryCommand(id, body.Kind, body.Description, body.Amount, body.Date, body.WalletId, body.Status, body.ReferenceMonth, body.Notes, body.Version),
                cancellationToken);
            return Results.Ok(EntryResponse.From(entry));
        });

        group.MapPatch("/{id:guid}/status", async (Guid id, EntryStatusRequest body, ChangeEntryStatusHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(EntryResponse.From(await handler.HandleAsync(id, body.Status, cancellationToken))));
    }
}
