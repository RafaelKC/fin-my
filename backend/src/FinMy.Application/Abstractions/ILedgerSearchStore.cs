using FinMy.Domain.Entries;

namespace FinMy.Application.Abstractions;

/// <summary>O que um item de busca é: Entrada, Saída ou Transferência.</summary>
public enum LedgerItemKind
{
    Income,
    Expense,
    Transfer,
}

/// <summary>Posição de um item na ordem da busca (data, criação e id, do mais recente ao mais antigo); base do cursor opaco.</summary>
public sealed record LedgerCursor(DateOnly Date, long CreatedAtUtcTicks, Guid Id);

/// <summary>Filtros já validados da busca (research R10). Listas vazias e <c>null</c> significam "sem filtro".</summary>
/// <param name="NormalizedText">Trecho já normalizado (sem acento, minúsculo); casa com a descrição normalizada.</param>
/// <param name="Status">Só existe em Entradas e Saídas: com ele, transferências ficam de fora.</param>
/// <param name="WalletIds">Entradas e Saídas da carteira; transferências com a carteira na origem ou no destino.</param>
/// <param name="After">Posição do último item da página anterior.</param>
public sealed record LedgerSearchCriteria(
    string? NormalizedText,
    IReadOnlyCollection<LedgerItemKind> Kinds,
    IReadOnlyCollection<Guid> WalletIds,
    EntryStatus? Status,
    DateOnly? From,
    DateOnly? To,
    LedgerCursor? After,
    int Limit);

/// <summary>Item da busca: <see cref="Entry"/> para Entrada/Saída, <see cref="Transfer"/> para transferência.</summary>
public sealed record LedgerItemView(LedgerItemKind Type, EntryView? Entry, TransferView? Transfer);

/// <summary>Página da busca; <paramref name="TotalCents"/> e <paramref name="TotalCount"/> valem para todos os resultados, não só a página.</summary>
public sealed record LedgerPage(IReadOnlyList<LedgerItemView> Items, long TotalCents, int TotalCount, LedgerCursor? Next);

/// <summary>Busca em Entradas, Saídas e Transferências do Espaço do contexto (FR-025).</summary>
public interface ILedgerSearchStore
{
    Task<LedgerPage> SearchAsync(LedgerSearchCriteria criteria, CancellationToken cancellationToken);
}
