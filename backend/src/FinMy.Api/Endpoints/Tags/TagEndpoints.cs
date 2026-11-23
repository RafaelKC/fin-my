using FinMy.Api.Idempotency;
using FinMy.Application.Tags;
using Microsoft.AspNetCore.Mvc;

namespace FinMy.Api.Endpoints.Tags;

/// <summary>Tags (004 US1/US5): <c>/tags*</c> de contracts/openapi.yaml.</summary>
public sealed class TagEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/tags").WithTags("tags").RequireAuthorization();

        group.MapGet("/", async ([FromQuery] string? archived, ListTagsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok((await handler.HandleAsync(archived, cancellationToken)).Select(TagResponse.From)));

        group.MapPost("/", async (TagRequest body, CreateTagHandler handler, CancellationToken cancellationToken) =>
        {
            var tag = TagResponse.From(await handler.HandleAsync(body.ToInput(), cancellationToken));
            return Results.Created($"/api/v1/tags/{tag.Id}", tag);
        }).WithIdempotency();

        group.MapPut("/{id:guid}", async (Guid id, UpdateTagRequest body, UpdateTagHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(TagResponse.From(await handler.HandleAsync(id, body.Name, body.Color, body.IsSettlement, body.Version, cancellationToken))));

        group.MapDelete("/{id:guid}", async (Guid id, DeleteTagHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/archive", async (Guid id, ArchiveTagHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(TagResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPost("/{id:guid}/unarchive", async (Guid id, UnarchiveTagHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(TagResponse.From(await handler.HandleAsync(id, cancellationToken))));

        group.MapPost("/{id:guid}/merge", async (Guid id, MergeTagRequest body, MergeTagHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(TagResponse.From(await handler.HandleAsync(id, body.TargetTagId, cancellationToken))));
    }
}
