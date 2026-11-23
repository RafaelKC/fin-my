using FinMy.Domain.Common;

namespace FinMy.Application.Cards;

/// <summary>Erros estáveis de <c>/credit-cards</c> (specs/003 contracts/openapi.yaml).</summary>
public static class CreditCardErrors
{
    public static readonly DomainError NotFound = new("not_found", "Cartão não encontrado.", DomainErrorKind.NotFound);

    public static readonly DomainError NameTaken = new(
        "credit_card.name_taken",
        "Já existe um cartão com esse nome.",
        DomainErrorKind.Conflict,
        new Dictionary<string, string[]> { ["name"] = ["Já existe um cartão com esse nome."] });

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Este cartão foi alterado em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    public static readonly DomainError HasPurchasesArchiveInstead = new(
        "credit_card.has_purchases_archive_instead", "Este cartão tem compras ou pagamentos. Você pode arquivá-lo.", DomainErrorKind.Conflict);

    public static readonly DomainError InvalidArchivedFilter = Invalid("validation_failed", "archived", "Use false, true ou all.");

    public static readonly DomainError DateRequired = Invalid("date.required", "date", "Informe a data.");

    public static readonly DomainError DateInvalid = Invalid("date.invalid", "date", "Data inválida. Use AAAA-MM-DD.");

    public static readonly DomainError DateOutOfRange = Invalid("date.out_of_range", "date", "A data deve estar entre 01/01/2000 e 31/12/2100.");

    public static readonly DomainError PaymentWalletRequired = Invalid(
        "wallet.required", "paymentWalletId", "Escolha a carteira de pagamento.");

    public static readonly DomainError PaymentWalletArchived = Invalid(
        "wallet.archived", "paymentWalletId", "Esta carteira está arquivada. Escolha outra carteira de pagamento.");

    public static readonly DomainError VersionRequired = Invalid("validation_failed", "version", "Informe a versão do cartão.");

    private static DomainError Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
