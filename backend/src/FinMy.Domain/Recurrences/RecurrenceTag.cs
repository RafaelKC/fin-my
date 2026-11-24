using FinMy.Domain.Common;

namespace FinMy.Domain.Recurrences;

/// <summary>Ligação Recorrência ↔ Tag comum (<c>recurrence_tags</c>); a Tag de acerto fica em <see cref="Recurrence.SettlementTagId"/>.</summary>
public sealed class RecurrenceTag : ITenantLink
{
    // Construtor para o EF Core.
    private RecurrenceTag()
    {
    }

    public RecurrenceTag(Guid recurrenceId, Guid tagId)
    {
        RecurrenceId = recurrenceId;
        TagId = tagId;
    }

    /// <summary>Preenchido pelo interceptor de gravação.</summary>
    public Guid TenantId { get; internal set; }

    public Guid RecurrenceId { get; private set; }

    public Guid TagId { get; private set; }
}
