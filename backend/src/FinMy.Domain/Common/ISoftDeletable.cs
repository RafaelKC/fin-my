namespace FinMy.Domain.Common;

/// <summary>Entidade com exclusão lógica.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }

    Guid? DeletionBatchId { get; }
}
