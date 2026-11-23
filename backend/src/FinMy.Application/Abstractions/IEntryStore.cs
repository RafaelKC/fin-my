using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Entrada ou Saída como o cliente a vê: <see cref="Version"/> é o token de concorrência (<c>xmin</c>, research R11).</summary>
public sealed record EntryView(
    Guid Id,
    EntryKind Kind,
    string Description,
    long AmountCents,
    DateOnly Date,
    YearMonth ReferenceMonth,
    EntryStatus Status,
    Guid WalletId,
    string? Notes,
    DateTimeOffset CreatedAt,
    long Version);

/// <summary>Carteira do Espaço do contexto, só com o que os lançamentos precisam saber.</summary>
public sealed record WalletRef(Guid Id, bool IsArchived);

/// <summary>Sugestão de descrição (FR-011, research R8).</summary>
public sealed record EntrySuggestion(string Description, int Uses, Guid? WalletId);

/// <summary>Persistência de Entradas e Saídas do Espaço do contexto (o filtro de Espaço é automático).</summary>
public interface IEntryStore
{
    /// <summary>Carteira do Espaço (inclusive arquivada); <c>null</c> se não existir ou for de outro Espaço.</summary>
    Task<WalletRef?> GetWalletAsync(Guid walletId, CancellationToken cancellationToken);

    Task<EntryView?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<EntryView> AddAsync(Entry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica <paramref name="change"/> ao lançamento e grava. <paramref name="expectedVersion"/> divergente da atual
    /// lança <see cref="Domain.Common.DomainException"/> de conflito de concorrência; <c>null</c> = última escrita vence.
    /// Devolve <c>null</c> se o lançamento não existir.
    /// </summary>
    Task<EntryView?> UpdateAsync(Guid id, long? expectedVersion, Action<Entry> change, CancellationToken cancellationToken);

    /// <summary>
    /// Até <paramref name="limit"/> descrições usadas desde <paramref name="notBefore"/> que contenham
    /// <paramref name="normalizedText"/> (já normalizado), por usos e uso mais recente. A carteira vem do lançamento mais
    /// recente com a mesma descrição e o mesmo <paramref name="kind"/>, se ainda ativa.
    /// </summary>
    Task<IReadOnlyList<EntrySuggestion>> SuggestAsync(
        string normalizedText, EntryKind kind, DateOnly notBefore, int limit, CancellationToken cancellationToken);
}
