using FinMy.Application.Abstractions;
using FinMy.Application.Transfers;

namespace FinMy.Api.Endpoints.Transfers;

/// <summary>Corpo de <c>POST /api/v1/transfers</c> (<c>TransferInput</c>); <c>Amount</c> é texto avaliado pelo servidor (RT-05).</summary>
public sealed record TransferRequest(
    Guid? FromWalletId,
    Guid? ToWalletId,
    string? Amount,
    DateOnly? Date,
    string? Description,
    string? Notes)
{
    internal TransferInput ToInput() => new(FromWalletId, ToWalletId, Amount, Date, Description, Notes);
}

/// <summary>Corpo de <c>PUT /api/v1/transfers/{id}</c>: <c>TransferInput</c> mais <c>version</c>.</summary>
public sealed record UpdateTransferRequest(
    Guid? FromWalletId,
    Guid? ToWalletId,
    string? Amount,
    DateOnly? Date,
    string? Description,
    string? Notes,
    long? Version)
{
    internal TransferInput ToInput() => new(FromWalletId, ToWalletId, Amount, Date, Description, Notes);
}

/// <summary>Resposta de uma transferência (<c>Transfer</c>).</summary>
public sealed record TransferResponse(
    Guid Id,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountCents,
    DateOnly Date,
    string Description,
    string? Notes,
    DateTimeOffset CreatedAt,
    long Version)
{
    internal static TransferResponse From(TransferView v) =>
        new(v.Id, v.FromWalletId, v.ToWalletId, v.AmountCents, v.Date, v.Description, v.Notes, v.CreatedAt, v.Version);
}

/// <summary>Resposta de uma exclusão (<c>DeletionResult</c>): o lote para desfazer.</summary>
public sealed record DeletionResponse(Guid DeletionBatchId);
