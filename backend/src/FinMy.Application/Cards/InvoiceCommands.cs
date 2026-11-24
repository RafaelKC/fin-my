namespace FinMy.Application.Cards;

/// <param name="Month"><c>AAAA-MM</c> do vencimento da fatura.</param>
/// <param name="Amount">Expressão RT-05; ausente = Restante (FR-017).</param>
/// <param name="Date">Ausente = hoje.</param>
/// <param name="WalletId">Ausente = carteira de pagamento do cartão.</param>
public sealed record RegisterInvoicePaymentCommand(Guid CreditCardId, string? Month, string? Amount, DateOnly? Date, Guid? WalletId);

/// <param name="Month"><c>AAAA-MM</c> do vencimento da fatura.</param>
/// <param name="Amount">Expressão RT-05 (o <c>=</c> inicial é aceito); ausente ou vazio remove a estimativa (007 R1).</param>
public sealed record SetInvoiceEstimateCommand(Guid CreditCardId, string? Month, string? Amount);

/// <param name="Version">Obrigatória; divergente da atual → conflito de concorrência (research R11).</param>
public sealed record UpdateInvoicePaymentCommand(Guid Id, string? Amount, DateOnly? Date, Guid? WalletId, long? Version);
