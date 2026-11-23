using FinMy.Domain.Common;

namespace FinMy.Domain.Tags;

/// <summary>Ligação Lançamento ↔ Tag comum (<c>entry_tags</c>). Só aceita tags com <c>is_settlement = false</c> (validado na aplicação).</summary>
public sealed class EntryTag : ITenantLink
{
    // Construtor para o EF Core.
    private EntryTag()
    {
    }

    public EntryTag(Guid entryId, Guid tagId)
    {
        EntryId = entryId;
        TagId = tagId;
    }

    /// <summary>Preenchido pelo interceptor de gravação.</summary>
    public Guid TenantId { get; internal set; }

    public Guid EntryId { get; private set; }

    public Guid TagId { get; private set; }
}
