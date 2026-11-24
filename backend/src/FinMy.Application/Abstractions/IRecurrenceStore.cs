using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Abstractions;

/// <summary>Persistência das recorrências do Espaço do contexto (005 US1) e das ocorrências de Entrada/Saída que elas geram.</summary>
public interface IRecurrenceStore
{
    /// <summary>Recorrência rastreada, ou <c>null</c> se não existe no Espaço.</summary>
    Task<Recurrence?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Todas as recorrências do Espaço, rastreadas, por descrição.</summary>
    Task<IReadOnlyList<Recurrence>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Recorrências que ainda têm meses a gerar até <paramref name="horizon"/> (research R2): nada gerado, ou o último mês
    /// gerado ainda antes do horizonte e do mês final.
    /// </summary>
    Task<IReadOnlyList<Recurrence>> ListPendingGenerationAsync(YearMonth horizon, CancellationToken cancellationToken);

    /// <summary>Tags de cada recorrência (a de acerto primeiro, depois as comuns).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetTagIdsAsync(IReadOnlyCollection<Recurrence> recurrences, CancellationToken cancellationToken);

    /// <summary>Menor data ≥ <paramref name="today"/> entre as ocorrências não excluídas de cada recorrência (só as que têm).</summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetNextOccurrenceDatesAsync(IReadOnlyCollection<Guid> ids, DateOnly today, CancellationToken cancellationToken);

    long VersionOf(Recurrence recurrence);

    /// <summary>Liga a recorrência nova às <paramref name="commonTagIds"/> (grava em <see cref="SaveAsync"/>).</summary>
    void Add(Recurrence recurrence, IReadOnlyCollection<Guid> commonTagIds);

    /// <summary>Acrescenta uma ocorrência de Entrada/Saída e suas tags comuns (grava em <see cref="SaveAsync"/>).</summary>
    void AddOccurrence(Entry entry, IReadOnlyCollection<Guid> commonTagIds);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<ICardTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
