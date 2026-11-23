using FinMy.Application.Abstractions;
using FinMy.Application.Wallets;

namespace FinMy.Api.Endpoints.Wallets;

/// <summary>Corpo de <c>POST /wallets</c> (contracts/openapi.yaml <c>WalletInput</c>). <c>initialBalance</c> é texto avaliado pelo servidor.</summary>
public sealed record WalletRequest(string? Name, string? Type, string? Color, string? InitialBalance, DateOnly? InitialBalanceDate)
{
    public WalletInput ToInput() => new(Name, Type, Color, InitialBalance, InitialBalanceDate);
}

/// <summary>Corpo de <c>PUT /wallets/{id}</c>: os campos de <see cref="WalletRequest"/> mais <c>version</c>.</summary>
public sealed record UpdateWalletRequest(string? Name, string? Type, string? Color, string? InitialBalance, DateOnly? InitialBalanceDate, long? Version)
{
    public WalletInput ToInput() => new(Name, Type, Color, InitialBalance, InitialBalanceDate);
}

/// <summary>Corpo opcional de <c>POST /wallets/{id}/archive</c>.</summary>
public sealed record ArchiveWalletRequest(Guid? NewPrimaryWalletId = null);

/// <summary>Resposta <c>Wallet</c> do contrato.</summary>
public sealed record WalletResponse(
    Guid Id,
    string Name,
    string Type,
    string Color,
    long InitialBalanceCents,
    DateOnly InitialBalanceDate,
    bool IsPrimary,
    bool Archived,
    long CurrentBalanceCents,
    bool HasEntries,
    long Version)
{
    public static WalletResponse From(WalletView w) => new(
        w.Id, w.Name, WalletTypeNames.ToText(w.Type), w.Color, w.InitialBalanceCents, w.InitialBalanceDate,
        w.IsPrimary, w.Archived, w.CurrentBalanceCents, w.HasEntries, w.Version);
}
