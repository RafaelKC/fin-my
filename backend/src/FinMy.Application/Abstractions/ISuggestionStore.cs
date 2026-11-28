using FinMy.Domain.Suggestions;

namespace FinMy.Application.Abstractions;

/// <summary>Sugestões de lançamento lidas de notificações de banco (010).</summary>
public interface ISuggestionStore
{
    Task<Suggestion?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Suggestion?> FindByDedupeKeyAsync(string dedupeKey, CancellationToken cancellationToken);

    /// <summary>Pendentes, as mais recentes primeiro.</summary>
    Task<IReadOnlyList<Suggestion>> ListPendingAsync(CancellationToken cancellationToken);

    void Add(Suggestion suggestion);

    Task SaveAsync(CancellationToken cancellationToken);
}
