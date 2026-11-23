using FinMy.Application.Abstractions;
using FinMy.Application.Tags;

namespace FinMy.Api.Endpoints.Tags;

/// <summary>Corpo de <c>POST /tags</c>.</summary>
public sealed record TagRequest(string? Name, string? Color, bool? IsSettlement)
{
    public TagInput ToInput() => new(Name, Color, IsSettlement);
}

/// <summary>Corpo de <c>PUT /tags/{id}</c>.</summary>
public sealed record UpdateTagRequest(string? Name, string? Color, bool? IsSettlement, long? Version);

/// <summary>Corpo de <c>POST /tags/{id}/merge</c>.</summary>
public sealed record MergeTagRequest(Guid? TargetTagId);

/// <summary>Resposta <c>Tag</c> do contrato.</summary>
public sealed record TagResponse(Guid Id, string Name, string Color, bool IsSettlement, bool Archived, int Uses90d, bool InUse, long Version)
{
    public static TagResponse From(TagView t) => new(t.Id, t.Name, t.Color, t.IsSettlement, t.Archived, t.Uses90d, t.InUse, t.Version);
}
