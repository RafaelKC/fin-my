using FinMy.Application.Deletions;

namespace FinMy.Api.Endpoints.Deletions;

/// <summary>Desfazer exclusão (RT-09): <c>POST /deletions/{batchId}/restore</c> (contracts/openapi.yaml).</summary>
public sealed class DeletionEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/deletions").WithTags("deletions").RequireAuthorization();

        group.MapPost("/{batchId}/restore", async (Guid batchId, RestoreDeletionHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(batchId, cancellationToken);
            return Results.NoContent();
        });
    }
}
