using FinMy.Api.Idempotency;
using FinMy.Application.Transfers;

namespace FinMy.Api.Endpoints.Transfers;

/// <summary>Transferências entre carteiras (US5): <c>/api/v1/transfers</c> (contracts/openapi.yaml).</summary>
public sealed class TransferEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/transfers").WithTags("transfers").RequireAuthorization();

        group.MapPost("/", async (TransferRequest body, CreateTransferHandler handler, CancellationToken cancellationToken) =>
        {
            var created = await handler.HandleAsync(body.ToInput(), cancellationToken);
            return Results.Created($"/api/v1/transfers/{created.Id}", TransferResponse.From(created));
        }).WithIdempotency();

        group.MapGet("/{id:guid}", async (Guid id, GetTransferHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(TransferResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPut("/{id:guid}", async (Guid id, UpdateTransferRequest body, UpdateTransferHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(TransferResponse.From(await handler.HandleAsync(id, body.ToInput(), body.Version, cancellationToken))));

        group.MapDelete("/{id:guid}", async (Guid id, DeleteTransferHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(new DeletionResponse(await handler.HandleAsync(id, cancellationToken))));
    }
}
