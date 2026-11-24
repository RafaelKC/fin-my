using System.Globalization;
using FinMy.Domain.Common;

namespace FinMy.Application.Cards;

/// <summary>Erros estáveis de fatura e pagamentos (specs/003 contracts/openapi.yaml).</summary>
public static class InvoiceErrors
{
    public static readonly DomainError PaymentNotFound = new("not_found", "Pagamento não encontrado.", DomainErrorKind.NotFound);

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Este pagamento foi alterado em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    public static readonly DomainError InvalidMonth = Invalid(
        "month.invalid", "month", "Use um mês entre 01/2000 e 12/2100, no formato AAAA-MM.");

    public static readonly DomainError NothingToPay = Invalid(
        "invoice.nothing_to_pay", "amount", "Esta fatura não tem valor em aberto.");

    public static readonly DomainError NoPayments = Invalid(
        "invoice.no_payments", "invoice", "Esta fatura não tem pagamentos.");

    public static readonly DomainError CarryOverNotAvailable = Invalid(
        "invoice.carry_over_not_available", "invoice", "Só é possível levar o restante depois do fechamento, e se houver restante.");

    public static readonly DomainError VersionRequired = Invalid("validation_failed", "version", "Informe a versão do pagamento.");

    public static readonly DomainError AmountRequired = Invalid("validation_failed", "amount", "Informe o valor do pagamento.");

    public static readonly DomainError DateRequired = Invalid("date.required", "date", "Informe a data do pagamento.");

    public static readonly DomainError WalletRequired = Invalid("wallet.required", "walletId", "Escolha a carteira do pagamento.");

    public static readonly DomainError WalletArchived = Invalid(
        "wallet.archived", "walletId", "Esta carteira está arquivada. Escolha outra carteira.");

    public static readonly DomainError WalletNotFound = new("not_found", "Carteira não encontrada.", DomainErrorKind.NotFound);

    /// <summary>O valor passa do Restante (R4): a mensagem traz o valor em aberto, "O valor em aberto é R$ 50,00".</summary>
    public static DomainError PaymentExceedsRemaining(long remainingCents) =>
        Invalid("invoice.payment_exceeds_remaining", "amount", $"O valor em aberto é {FormatBrl(remainingCents)}.");

    /// <summary>R$ 1.234,56 (sem depender da cultura do servidor).</summary>
    public static string FormatBrl(long cents)
    {
        var sign = cents < 0 ? "-" : string.Empty;
        var abs = Math.Abs(cents);
        var whole = (abs / 100).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{sign}R$ {whole},{abs % 100:00}";
    }

    /// <summary>Erro de validação de um campo (422).</summary>
    public static DomainError Field(string code, string field, string message) => Invalid(code, field, message);

    private static DomainError Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
