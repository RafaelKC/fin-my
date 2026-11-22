namespace FinMy.Domain.Common;

/// <summary>Entidade que pode ser arquivada (some das listas ativas, sem ser excluída).</summary>
public interface IArchivable
{
    DateTimeOffset? ArchivedAt { get; }
}
