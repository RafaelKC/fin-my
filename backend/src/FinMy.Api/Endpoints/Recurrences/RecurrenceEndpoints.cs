using FinMy.Api.Idempotency;
using FinMy.Application.Recurrences;

namespace FinMy.Api.Endpoints.Recurrences;

/// <summary>Recorrências (005 US1): <c>GET|POST /recurrences</c> e <c>GET /recurrences/{id}</c> (specs/005 contracts/api.md).</summary>
public sealed class RecurrenceEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/recurrences").WithTags("recurrences").RequireAuthorization();

        group.MapGet("/", async (string? status, ListRecurrencesHandler handler, CancellationToken cancellationToken) =>
        {
            var list = await handler.HandleAsync(status, cancellationToken);
            return Results.Ok(new RecurrenceListResponse(
                list.Items.Select(RecurrenceResponse.From).ToList(), list.MonthlyIncomeCents, list.MonthlyExpenseCents));
        });

        group.MapPost("/", async (RecurrenceRequest body, CreateRecurrenceHandler handler, CancellationToken cancellationToken) =>
        {
            var recurrence = RecurrenceResponse.From(await handler.HandleAsync(body.ToCreateCommand(), cancellationToken));
            return Results.Created($"/api/v1/recurrences/{recurrence.Id}", recurrence);
        }).WithIdempotency();

        group.MapGet("/{id:guid}", async (Guid id, GetRecurrenceHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(RecurrenceResponse.From(await handler.HandleAsync(id, cancellationToken))));
    }
}
