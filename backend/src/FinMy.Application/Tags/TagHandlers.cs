using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Tags;

namespace FinMy.Application.Tags;

/// <summary>Campos de <c>POST /tags</c>.</summary>
public sealed record TagInput(string? Name, string? Color, bool? IsSettlement);

/// <summary>Lista as tags com <c>uses90d</c> e <c>inUse</c> (research R2).</summary>
public sealed class ListTagsHandler(ITagStore tags, IClock clock)
{
    public Task<IReadOnlyList<TagView>> HandleAsync(string? archived, CancellationToken cancellationToken)
    {
        var filter = archived switch
        {
            null or "false" => TagArchivedFilter.ActiveOnly,
            "true" => TagArchivedFilter.ArchivedOnly,
            "all" => TagArchivedFilter.All,
            _ => throw new DomainException(TagErrors.InvalidArchivedFilter),
        };

        return tags.ListAsync(filter, clock.Today, cancellationToken);
    }
}

/// <summary>Cria tag; nome repetido (normalizado) → 409 com <c>existingTagId</c> (US1.3).</summary>
public sealed class CreateTagHandler(ITagStore tags, IClock clock)
{
    public async Task<TagView> HandleAsync(TagInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var color = string.IsNullOrWhiteSpace(input.Color) ? TagPalette.Next(await tags.CountAsync(cancellationToken)) : input.Color;
        var tag = Tag.Create(input.Name ?? string.Empty, color, input.IsSettlement ?? false);

        if (await tags.FindIdByNormalizedNameAsync(tag.NameNormalized, null, cancellationToken) is { } existing)
        {
            throw new DomainException(TagErrors.NameTaken(existing));
        }

        tags.Add(tag);
        await tags.SaveAsync(cancellationToken);
        return await tags.GetAsync(tag.Id, clock.Today, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
    }
}

/// <summary>
/// Edita nome, cor e <c>isSettlement</c> (research R7). Trocar <c>isSettlement</c> move as ligações entre a tabela
/// de ligação e a coluna <c>settlement_tag_id</c> na mesma gravação; marcar com outra tag de acerto no mesmo
/// lançamento → 409 <c>tag.settlement_conflict</c>.
/// </summary>
public sealed class UpdateTagHandler(ITagStore tags, IClock clock)
{
    public async Task<TagView> HandleAsync(Guid id, string? name, string? color, bool? isSettlement, long? version, CancellationToken cancellationToken)
    {
        var tag = await tags.FindAsync(id, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
        if (version is null)
        {
            throw new DomainException(TagErrors.VersionRequired);
        }

        if (tags.VersionOf(tag) != version)
        {
            throw new DomainException(TagErrors.ConcurrencyConflict);
        }

        tags.ExpectVersion(tag, version.Value);
        tag.Rename(name ?? string.Empty);
        tag.ChangeColor(color ?? string.Empty);

        if (await tags.FindIdByNormalizedNameAsync(tag.NameNormalized, id, cancellationToken) is { } existing)
        {
            throw new DomainException(TagErrors.NameTaken(existing));
        }

        if (isSettlement is { } wanted && wanted != tag.IsSettlement)
        {
            if (wanted)
            {
                var conflicts = await tags.ConflictsOnMarkAsync(id, cancellationToken);
                if (conflicts.Count > 0)
                {
                    throw new DomainException(TagErrors.SettlementConflict(conflicts));
                }

                await tags.MoveLinksToSettlementColumnAsync(id, cancellationToken);
                tag.MarkSettlement();
            }
            else
            {
                await tags.MoveSettlementColumnToLinksAsync(id, cancellationToken);
                tag.UnmarkSettlement();
            }
        }

        await tags.SaveAsync(cancellationToken);
        return await tags.GetAsync(id, clock.Today, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
    }
}

public sealed class ArchiveTagHandler(ITagStore tags, IClock clock)
{
    public async Task<TagView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await tags.FindAsync(id, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
        tag.Archive(clock.UtcNow);
        await tags.SaveAsync(cancellationToken);
        return await tags.GetAsync(id, clock.Today, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
    }
}

public sealed class UnarchiveTagHandler(ITagStore tags, IClock clock)
{
    public async Task<TagView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await tags.FindAsync(id, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
        tag.Unarchive();
        await tags.SaveAsync(cancellationToken);
        return await tags.GetAsync(id, clock.Today, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
    }
}

/// <summary>Exclui só tag sem uso (US5.3); com uso → 409 <c>tag.in_use_archive_instead</c>.</summary>
public sealed class DeleteTagHandler(ITagStore tags)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await tags.FindAsync(id, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
        if (await tags.IsInUseAsync(id, cancellationToken))
        {
            throw new DomainException(TagErrors.InUseArchiveInstead);
        }

        tags.Remove(tag);
        await tags.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Junta a tag no destino (US5.1, US5.4): aplica o destino onde havia a origem (sem duplicar), recusa se algum
/// lançamento ficaria com duas Tags de acerto e exclui a origem (soft delete).
/// </summary>
public sealed class MergeTagHandler(ITagStore tags, IClock clock)
{
    public async Task<TagView> HandleAsync(Guid id, Guid? targetTagId, CancellationToken cancellationToken)
    {
        var source = await tags.FindAsync(id, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
        if (targetTagId is null)
        {
            throw new DomainException(TagErrors.MergeIntoSelf);
        }

        if (targetTagId == id)
        {
            throw new DomainException(TagErrors.MergeIntoSelf);
        }

        var target = await tags.FindAsync(targetTagId.Value, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);

        if (target.IsSettlement)
        {
            var conflicts = await tags.ConflictsOnMergeAsync(id, target.Id, cancellationToken);
            if (conflicts.Count > 0)
            {
                throw new DomainException(TagErrors.SettlementConflict(conflicts));
            }
        }

        await tags.ReplaceTagAsync(id, target, cancellationToken);
        tags.Remove(source);
        await tags.SaveAsync(cancellationToken);
        return await tags.GetAsync(target.Id, clock.Today, cancellationToken) ?? throw new DomainException(TagErrors.NotFound);
    }
}
