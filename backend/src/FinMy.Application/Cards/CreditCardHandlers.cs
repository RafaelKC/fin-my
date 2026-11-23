using FinMy.Application.Abstractions;
using FinMy.Application.Wallets;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>Cria cartão (US1.1): carteira de pagamento ativa; nome único sem diferenciar caixa e acento (US1.2).</summary>
public sealed class CreateCreditCardHandler(ICreditCardStore cards, IWalletStore wallets)
{
    public async Task<CreditCardView> HandleAsync(CreditCardInput input, CancellationToken cancellationToken)
    {
        var parsed = CreditCardInputParser.Parse(input);
        await CreditCardWallets.EnsureActiveAsync(wallets, parsed.PaymentWalletId, cancellationToken);

        var card = CreditCard.Create(parsed.Name, parsed.Color, parsed.ClosingDay, parsed.DueDay, parsed.PaymentWalletId, parsed.Limit);
        cards.Add(card);
        await cards.SaveAsync(cancellationToken);

        return await cards.GetAsync(card.Id, cancellationToken) ?? throw new InvalidOperationException("Cartão criado não encontrado.");
    }
}

/// <summary>Lista cartões com fatura atual, próxima, faturas em aberto e limite disponível (FR-025, FR-026).</summary>
public sealed class ListCreditCardsHandler(ICreditCardStore cards, IClock clock)
{
    public Task<IReadOnlyList<CreditCardOverviewView>> HandleAsync(string? archived, CancellationToken cancellationToken)
    {
        var filter = archived switch
        {
            null or "" or "false" => CreditCardArchivedFilter.ActiveOnly,
            "true" => CreditCardArchivedFilter.ArchivedOnly,
            "all" => CreditCardArchivedFilter.All,
            _ => throw new DomainException(CreditCardErrors.InvalidArchivedFilter),
        };

        return cards.ListOverviewsAsync(filter, clock.Today, cancellationToken);
    }
}

/// <summary>Obtém um cartão (ativo ou arquivado) com o resumo das faturas.</summary>
public sealed class GetCreditCardHandler(ICreditCardStore cards, IClock clock)
{
    public async Task<CreditCardOverviewView> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await cards.GetOverviewAsync(id, clock.Today, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
}

/// <summary>Arquiva (RT-11): o cartão some da escolha de novas compras; faturas continuam visíveis e pagáveis.</summary>
public sealed class ArchiveCreditCardHandler(ICreditCardStore cards, IClock clock)
{
    public async Task<CreditCardView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        if (!card.IsArchived)
        {
            card.Archive(clock.UtcNow);
            await cards.SaveAsync(cancellationToken);
        }

        return await cards.GetAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
    }
}

/// <summary>Desarquiva. O nome continua reservado enquanto arquivado, então não há conflito possível (índice único).</summary>
public sealed class UnarchiveCreditCardHandler(ICreditCardStore cards)
{
    public async Task<CreditCardView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        if (card.IsArchived)
        {
            card.Unarchive();
            await cards.SaveAsync(cancellationToken);
        }

        return await cards.GetAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
    }
}

/// <summary>Exclui o cartão só se nunca teve compras nem pagamentos (RT-11, US1.3); senão 409 e arquivar.</summary>
public sealed class DeleteCreditCardHandler(ICreditCardStore cards)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        if (await cards.HasPurchasesOrPaymentsAsync(id, cancellationToken))
        {
            throw new DomainException(CreditCardErrors.HasPurchasesArchiveInstead);
        }

        cards.Remove(card);
        await cards.SaveAsync(cancellationToken);
    }
}

/// <summary>Em qual fatura cai uma compra nesta data (conveniência; o front também calcula pela porta TS). Datas gravadas, se a linha existe.</summary>
public sealed class GetInvoiceForHandler(ICreditCardStore cards)
{
    public async Task<InvoiceRefView> HandleAsync(Guid id, string? date, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        var purchaseDate = ParseDate(date);

        var month = InvoiceCycle.InvoiceFor(purchaseDate, card.ClosingDay, card.DueDay);
        var row = await cards.FindInvoiceAsync(id, month, cancellationToken);
        return new InvoiceRefView(
            id,
            month,
            row?.ClosingDate ?? InvoiceCycle.ClosingDate(month, card.ClosingDay, card.DueDay),
            row?.DueDate ?? InvoiceCycle.DueDate(month, card.DueDay));
    }

    private static DateOnly ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainException(CreditCardErrors.DateRequired);
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
        {
            throw new DomainException(CreditCardErrors.DateInvalid);
        }

        return date >= new DateOnly(2000, 1, 1) && date <= new DateOnly(2100, 12, 31)
            ? date
            : throw new DomainException(CreditCardErrors.DateOutOfRange);
    }
}

/// <summary>Referência de fatura (contrato <c>InvoiceRef</c>).</summary>
public sealed record InvoiceRefView(Guid CreditCardId, YearMonth DueMonth, DateOnly ClosingDate, DateOnly DueDate);

internal static class CreditCardWallets
{
    /// <summary>A carteira de pagamento tem de existir no Espaço e estar ativa (FR-001).</summary>
    public static async Task EnsureActiveAsync(IWalletStore wallets, Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(walletId, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
        if (wallet.IsArchived)
        {
            throw new DomainException(CreditCardErrors.PaymentWalletArchived);
        }
    }
}
