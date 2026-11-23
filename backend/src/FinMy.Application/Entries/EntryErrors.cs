using FinMy.Domain.Common;

namespace FinMy.Application.Entries;

/// <summary>Erros estáveis de <c>/entries</c> (contracts/openapi.yaml).</summary>
public static class EntryErrors
{
    public static readonly DomainError NotFound = new("not_found", "Lançamento não encontrado.", DomainErrorKind.NotFound);

    public static readonly DomainError WalletNotFound = new("not_found", "Carteira não encontrada.", DomainErrorKind.NotFound);

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Este lançamento foi alterado em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    public static DomainError WalletArchived { get; } = Field("wallet.archived", "walletId", "Esta carteira está arquivada");

    public static DomainError Field(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
