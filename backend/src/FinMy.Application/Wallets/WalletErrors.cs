using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>Erros estáveis de <c>/wallets</c> (contracts/openapi.yaml).</summary>
public static class WalletErrors
{
    public static readonly DomainError NotFound = new("not_found", "Carteira não encontrada.", DomainErrorKind.NotFound);

    public static readonly DomainError NameTaken = new(
        "wallet.name_taken",
        "Já existe uma carteira com esse nome.",
        DomainErrorKind.Conflict,
        new Dictionary<string, string[]> { ["name"] = ["Já existe uma carteira com esse nome."] });

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Esta carteira foi alterada em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    public static readonly DomainError HasEntriesArchiveInstead = new(
        "wallet.has_entries_archive_instead", "Esta carteira tem lançamentos. Você pode arquivá-la.", DomainErrorKind.Conflict);

    public static readonly DomainError NewPrimaryRequired = Invalid(
        "wallet.new_primary_required", "newPrimaryWalletId", "Escolha outra carteira para ser a principal.");

    public static readonly DomainError LastActiveCannotBeArchived = Invalid(
        "wallet.last_active_cannot_be_archived", "walletId", "Cadastre outra carteira antes de arquivar esta: ela é a única ativa.");

    public static readonly DomainError InvalidNewPrimary = Invalid(
        "wallet.invalid_new_primary", "newPrimaryWalletId", "A nova carteira principal precisa estar ativa e ser outra carteira.");

    public static readonly DomainError Archived = Invalid(
        "wallet.archived", "walletId", "Uma carteira arquivada não pode ser a principal. Desarquive-a antes.");

    public static readonly DomainError InvalidArchivedFilter = Invalid(
        "validation_failed", "archived", "Use false, true ou all.");

    private static DomainError Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
