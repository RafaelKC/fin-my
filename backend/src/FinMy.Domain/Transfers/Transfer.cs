using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Transfers;

/// <summary>
/// Transferência entre duas Carteiras (<c>transfers</c>, 002 FR-021). Não é Entrada nem Saída; o mês
/// é o da própria data (RT-04).
/// </summary>
public sealed class Transfer : TenantEntity
{
    /// <summary>Descrição usada quando a pessoa não informa uma.</summary>
    public const string DefaultDescription = "Transferência";

    // Construtor para o EF Core.
    private Transfer()
    {
    }

    private Transfer(Guid fromWalletId, Guid toWalletId, Money amount, DateOnly date, string? description, string? notes)
    {
        Apply(fromWalletId, toWalletId, amount, date, description, notes);
    }

    public Guid FromWalletId { get; private set; }

    public Guid ToWalletId { get; private set; }

    public long AmountCents { get; private set; }

    public DateOnly TransferDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string DescriptionNormalized { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public Money Amount => new(AmountCents);

    public static Transfer Create(
        Guid fromWalletId,
        Guid toWalletId,
        Money amount,
        DateOnly date,
        string? description,
        string? notes) =>
        new(fromWalletId, toWalletId, amount, date, description, notes);

    /// <summary>Substitui todos os campos editáveis (PUT); as mesmas regras da criação valem, e nada muda se alguma falhar.</summary>
    public void Update(
        Guid fromWalletId,
        Guid toWalletId,
        Money amount,
        DateOnly date,
        string? description,
        string? notes) =>
        Apply(fromWalletId, toWalletId, amount, date, description, notes);

    private void Apply(Guid fromWalletId, Guid toWalletId, Money amount, DateOnly date, string? description, string? notes)
    {
        if (fromWalletId == toWalletId)
        {
            throw Rules.Invalid("transfer.same_wallet", "toWalletId", "Escolha carteiras diferentes na origem e no destino");
        }

        var amountCents = Rules.EntryAmount(amount);
        var transferDate = Rules.Date(date, "transferDate");
        var finalDescription = string.IsNullOrWhiteSpace(description)
            ? DefaultDescription
            : Rules.RequiredText(description, Rules.DescriptionMaxLength, "description", "description", "a descrição");
        var finalNotes = Rules.OptionalText(notes, Rules.NotesMaxLength, "notes", "notes", "a observação");

        FromWalletId = fromWalletId;
        ToWalletId = toWalletId;
        AmountCents = amountCents;
        TransferDate = transferDate;
        Description = finalDescription;
        DescriptionNormalized = TextNormalizer.Normalize(finalDescription);
        Notes = finalNotes;
    }
}
