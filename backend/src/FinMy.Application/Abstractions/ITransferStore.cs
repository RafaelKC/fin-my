using FinMy.Domain.Common;
using FinMy.Domain.Transfers;

namespace FinMy.Application.Abstractions;

/// <summary>Situação de uma carteira para quem vai usá-la numa transferência.</summary>
public enum WalletAvailability
{
    /// <summary>Não existe no Espaço (inclusive de outro Espaço ou excluída).</summary>
    NotFound,

    /// <summary>Existe e está ativa.</summary>
    Active,

    /// <summary>Existe, mas está arquivada.</summary>
    Archived,
}

/// <summary>Resultado de <see cref="ITransferStore.UpdateAsync"/>.</summary>
public enum TransferUpdateOutcome
{
    Updated,
    NotFound,

    /// <summary>A <c>version</c> informada não é mais a atual (research R11).</summary>
    VersionConflict,
}

/// <summary>Transferência como a API a devolve; <paramref name="Version"/> é o <c>xmin</c> da linha (research R11).</summary>
public sealed record TransferView(
    Guid Id,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountCents,
    DateOnly Date,
    string Description,
    string? Notes,
    DateTimeOffset CreatedAt,
    Channel CreatedVia,
    long Version);

/// <summary>Persistência das transferências (US5, FR-021..FR-023), sempre dentro do Espaço do contexto.</summary>
public interface ITransferStore
{
    Task<WalletAvailability> GetWalletAvailabilityAsync(Guid walletId, CancellationToken cancellationToken);

    Task<TransferView> AddAsync(Transfer transfer, CancellationToken cancellationToken);

    Task<TransferView?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica <paramref name="apply"/> à transferência se <paramref name="version"/> ainda for a atual. Uma exceção de
    /// <paramref name="apply"/> (regra de domínio) sobe sem gravar nada.
    /// </summary>
    Task<(TransferUpdateOutcome Outcome, TransferView? Transfer)> UpdateAsync(
        Guid id, long version, Action<Transfer> apply, CancellationToken cancellationToken);

    /// <summary>Exclusão lógica no lote corrente do contexto; <c>false</c> se a transferência não existe.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
