using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;

namespace FinMy.Application.Transfers;

/// <summary>Dados de criação/edição de uma transferência (contracts/openapi.yaml <c>TransferInput</c>). <c>Amount</c> é a expressão do campo de valor (RT-05).</summary>
public sealed record TransferInput(
    Guid? FromWalletId,
    Guid? ToWalletId,
    string? Amount,
    DateOnly? Date,
    string? Description,
    string? Notes);

/// <summary>Erros e validações compartilhados pelos casos de uso de transferência.</summary>
public static class TransferErrors
{
    public static readonly DomainError NotFound = new("not_found", "Não encontramos a transferência.", DomainErrorKind.NotFound);

    public static readonly DomainError WalletNotFound = new("not_found", "Não encontramos a carteira.", DomainErrorKind.NotFound);

    public static readonly DomainError VersionConflict = new(
        "concurrency_conflict", "Esta transferência foi alterada em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    private static DomainError Required(string field, string message) => new(
        "validation_failed", message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });

    internal static DomainError WalletArchived(string field) => new(
        "wallet.archived", "Esta carteira está arquivada. Escolha outra.", DomainErrorKind.Validation,
        new Dictionary<string, string[]> { [field] = ["Esta carteira está arquivada. Escolha outra."] });

    /// <summary>Confere os campos obrigatórios e avalia o valor (RT-05); o resto das regras é do <see cref="Transfer"/>.</summary>
    internal static (Guid From, Guid To, Money Amount, DateOnly Date) Validate(TransferInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new Dictionary<string, string[]>();
        if (input.FromWalletId is null || input.FromWalletId == Guid.Empty)
        {
            errors["fromWalletId"] = ["Escolha a carteira de origem"];
        }

        if (input.ToWalletId is null || input.ToWalletId == Guid.Empty)
        {
            errors["toWalletId"] = ["Escolha a carteira de destino"];
        }

        if (input.Date is null)
        {
            errors["date"] = ["Informe a data"];
        }

        if (errors.Count > 0)
        {
            throw new DomainException(new DomainError(
                "validation_failed", "Alguns dados estão inválidos. Corrija os campos indicados.", DomainErrorKind.Validation, errors));
        }

        var amount = ValueExpressionParser.Parse(input.Amount);
        if (!amount.IsSuccess)
        {
            throw new DomainException(ValueExpressionErrors.ToDomainError(amount.Error!, "amount"));
        }

        return (input.FromWalletId!.Value, input.ToWalletId!.Value, amount.Money, input.Date!.Value);
    }

    internal static DomainError RequiredVersion() => Required("version", "Informe a versão da transferência");
}

/// <summary>Cria uma transferência (FR-021): carteiras diferentes e ativas, valor pelo parser RT-05, descrição padrão "Transferência".</summary>
public sealed class CreateTransferHandler(ITransferStore store)
{
    public async Task<TransferView> HandleAsync(TransferInput input, CancellationToken cancellationToken)
    {
        var (from, to, amount, date) = TransferErrors.Validate(input);
        var transfer = Transfer.Create(from, to, amount, date, input.Description, input.Notes);

        await EnsureUsableAsync(store, from, "fromWalletId", cancellationToken);
        await EnsureUsableAsync(store, to, "toWalletId", cancellationToken);

        return await store.AddAsync(transfer, cancellationToken);
    }

    internal static async Task EnsureUsableAsync(ITransferStore store, Guid walletId, string field, CancellationToken cancellationToken)
    {
        switch (await store.GetWalletAvailabilityAsync(walletId, cancellationToken))
        {
            case WalletAvailability.NotFound:
                throw new DomainException(TransferErrors.WalletNotFound);
            case WalletAvailability.Archived:
                throw new DomainException(TransferErrors.WalletArchived(field));
        }
    }
}

/// <summary>Obtém uma transferência do Espaço.</summary>
public sealed class GetTransferHandler(ITransferStore store)
{
    public async Task<TransferView> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken) ?? throw new DomainException(TransferErrors.NotFound);
}

/// <summary>
/// Edita uma transferência com <c>version</c> (research R11). Só as carteiras que mudaram precisam estar ativas:
/// editar o valor de uma transferência antiga não deve falhar porque a carteira foi arquivada depois.
/// </summary>
public sealed class UpdateTransferHandler(ITransferStore store)
{
    public async Task<TransferView> HandleAsync(Guid id, TransferInput input, long? version, CancellationToken cancellationToken)
    {
        var current = await store.GetAsync(id, cancellationToken) ?? throw new DomainException(TransferErrors.NotFound);

        var (from, to, amount, date) = TransferErrors.Validate(input);
        if (version is null)
        {
            throw new DomainException(TransferErrors.RequiredVersion());
        }

        if (version != current.Version)
        {
            throw new DomainException(TransferErrors.VersionConflict);
        }

        // Valida as regras da própria transferência (carteiras diferentes, texto, data) antes de consultar carteiras.
        Transfer.Create(from, to, amount, date, input.Description, input.Notes);

        if (from != current.FromWalletId)
        {
            await CreateTransferHandler.EnsureUsableAsync(store, from, "fromWalletId", cancellationToken);
        }

        if (to != current.ToWalletId)
        {
            await CreateTransferHandler.EnsureUsableAsync(store, to, "toWalletId", cancellationToken);
        }

        var (outcome, updated) = await store.UpdateAsync(
            id, version.Value, t => t.Update(from, to, amount, date, input.Description, input.Notes), cancellationToken);

        return outcome switch
        {
            TransferUpdateOutcome.Updated => updated!,
            TransferUpdateOutcome.VersionConflict => throw new DomainException(TransferErrors.VersionConflict),
            _ => throw new DomainException(TransferErrors.NotFound),
        };
    }
}

/// <summary>Exclui (lógico) uma transferência num lote próprio, que a pessoa pode desfazer por <c>/deletions/{batchId}/restore</c> (RT-09).</summary>
public sealed class DeleteTransferHandler(ITransferStore store, ICurrentContext context)
{
    public async Task<Guid> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var batchId = context.BeginDeletionBatch();
        if (!await store.DeleteAsync(id, cancellationToken))
        {
            throw new DomainException(TransferErrors.NotFound);
        }

        return batchId;
    }
}
