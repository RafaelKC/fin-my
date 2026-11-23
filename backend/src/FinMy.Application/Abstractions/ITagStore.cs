using FinMy.Domain.Tags;

namespace FinMy.Application.Abstractions;

/// <summary>Tag com os agregados da lista (research R2): uso nos últimos 90 dias e se já está em uso.</summary>
/// <param name="Version">Token de concorrência (<c>xmin</c>) como número.</param>
public sealed record TagView(Guid Id, string Name, string Color, bool IsSettlement, bool Archived, int Uses90d, bool InUse, long Version);

/// <summary>Lançamento ou compra que impede uma mudança de Tag de acerto (<c>tag.settlement_conflict</c>).</summary>
public sealed record TagConflict(string Type, Guid Id, string Description, DateOnly Date);

public enum TagArchivedFilter
{
    ActiveOnly,
    ArchivedOnly,
    All,
}

/// <summary>Persistência das tags do Espaço do contexto (004 US1/US5).</summary>
public interface ITagStore
{
    Task<Tag?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Id da tag (qualquer estado) com este nome normalizado, exceto <paramref name="exceptId"/>.</summary>
    Task<Guid?> FindIdByNormalizedNameAsync(string normalizedName, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Quantas tags existem (define a próxima cor da paleta).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    Task<TagView?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<TagView>> ListAsync(TagArchivedFilter filter, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Lançamentos/compras (mesmo excluídos) que usam a tag, como Tag de acerto ou comum.</summary>
    Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Itens que têm a tag como comum e já têm outra Tag de acerto (impede marcá-la como de acerto).</summary>
    Task<IReadOnlyList<TagConflict>> ConflictsOnMarkAsync(Guid tagId, CancellationToken cancellationToken);

    /// <summary>Itens com a tag de origem que ficariam com duas Tags de acerto ao juntar na de destino (de acerto).</summary>
    Task<IReadOnlyList<TagConflict>> ConflictsOnMergeAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken);

    /// <summary>Move as ligações da tag para a coluna de acerto dos itens (rastreado; grava em <see cref="SaveAsync"/>).</summary>
    Task MoveLinksToSettlementColumnAsync(Guid tagId, CancellationToken cancellationToken);

    /// <summary>Move a coluna de acerto da tag para ligações comuns (rastreado).</summary>
    Task MoveSettlementColumnToLinksAsync(Guid tagId, CancellationToken cancellationToken);

    /// <summary>Troca a origem pelo destino nos itens, sem duplicar; o destino fica onde sua natureza manda (rastreado).</summary>
    Task ReplaceTagAsync(Guid sourceId, Tag target, CancellationToken cancellationToken);

    long VersionOf(Tag tag);

    void ExpectVersion(Tag tag, long version);

    void Add(Tag tag);

    void Remove(Tag tag);

    /// <summary>Grava; nome repetido vira <c>tag.name_taken</c> (409).</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
