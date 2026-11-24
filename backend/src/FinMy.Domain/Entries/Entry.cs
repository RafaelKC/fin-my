using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Entries;

/// <summary>Entrada ou Saída (<c>entries</c>, 002 FR-005). Valor sempre positivo; o sentido vem de <see cref="Kind"/>.</summary>
public sealed class Entry : TenantEntity
{
    // Construtor para o EF Core.
    private Entry()
    {
    }

    private Entry(
        EntryKind kind,
        string description,
        Money amount,
        DateOnly date,
        Guid walletId,
        EntryStatus status,
        YearMonth referenceMonth,
        string? notes)
    {
        Kind = kind;
        SetDescription(description);
        SetAmount(amount);
        EntryDate = Rules.Date(date, "entryDate");
        WalletId = walletId;
        Status = status;
        SetReferenceMonth(referenceMonth);
        SetNotes(notes);
    }

    public EntryKind Kind { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string DescriptionNormalized { get; private set; } = string.Empty;

    public long AmountCents { get; private set; }

    public DateOnly EntryDate { get; private set; }

    /// <summary>Mês de referência (RT-04): sempre o dia 1 do mês.</summary>
    public DateOnly ReferenceMonth { get; private set; }

    public EntryStatus Status { get; private set; }

    public Guid WalletId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Tag de acerto do lançamento (no máximo uma, 004 FR-002); a aplicação garante que a tag tem <c>is_settlement</c>.</summary>
    public Guid? SettlementTagId { get; private set; }

    /// <summary>Mesmo valor nas partes de uma divisão (004 FR-014).</summary>
    public Guid? SplitGroupId { get; private set; }

    /// <summary>Recorrência de origem (005); nula em lançamentos comuns.</summary>
    public Guid? RecurrenceId { get; private set; }

    /// <summary>Mês da ocorrência na recorrência (dia 1); nulo se <see cref="RecurrenceId"/> é nulo.</summary>
    public DateOnly? RecurrenceMonth { get; private set; }

    /// <summary>Ocorrência alterada à mão (005 FR-005): a recorrência não a sobrescreve.</summary>
    public bool RecurrenceModified { get; private set; }

    public Money Amount => new(AmountCents);

    public void LinkToRecurrence(Guid recurrenceId, YearMonth month)
    {
        RecurrenceId = recurrenceId;
        RecurrenceMonth = month.FirstDay;
    }

    /// <summary>Marca como "alterada" uma ocorrência de recorrência (005 FR-005); sem efeito em lançamentos comuns.</summary>
    public void MarkRecurrenceModified()
    {
        if (RecurrenceId is not null)
        {
            RecurrenceModified = true;
        }
    }

    /// <summary>Desfaz o vínculo com a recorrência: o lançamento vira comum (005 FR-007).</summary>
    public void UnlinkFromRecurrence()
    {
        RecurrenceId = null;
        RecurrenceMonth = null;
        RecurrenceModified = false;
    }

    /// <summary>
    /// Valores cuja mudança torna a ocorrência "alterada" (005 research R3): tudo menos situação (e as tags, que quem chama compara).
    /// </summary>
    public (EntryKind Kind, string Description, long AmountCents, DateOnly Date, DateOnly ReferenceMonth, Guid WalletId, string? Notes, Guid? SettlementTagId) RecurrenceContent =>
        (Kind, Description, AmountCents, EntryDate, ReferenceMonth, WalletId, Notes, SettlementTagId);

    public YearMonth ReferenceYearMonth => YearMonth.From(ReferenceMonth);

    /// <summary>
    /// Situação padrão (FR-012): data ≤ hoje → Recebida/Paga; data &gt; hoje → Prevista.
    /// <paramref name="today"/> vem de <c>IClock.Today</c> (RT-03).
    /// </summary>
    public void ChangeSettlementTag(Guid? tagId) => SettlementTagId = tagId;

    public void ChangeSplitGroup(Guid? splitGroupId) => SplitGroupId = splitGroupId;

    public static EntryStatus DefaultStatus(DateOnly date, DateOnly today) =>
        date <= today ? EntryStatus.Settled : EntryStatus.Planned;

    /// <summary>
    /// Cria um lançamento. Sem <paramref name="status"/>, vale a situação padrão (FR-012); sem
    /// <paramref name="referenceMonth"/>, o mês da data (RT-04).
    /// </summary>
    public static Entry Create(
        EntryKind kind,
        string description,
        Money amount,
        DateOnly date,
        Guid walletId,
        EntryStatus? status,
        YearMonth? referenceMonth,
        string? notes,
        DateOnly today) =>
        new(
            kind,
            description,
            amount,
            date,
            walletId,
            status ?? DefaultStatus(date, today),
            referenceMonth ?? YearMonth.From(date),
            notes);

    public void ChangeDescription(string description) => SetDescription(description);

    public void ChangeAmount(Money amount) => SetAmount(amount);

    public void ChangeNotes(string? notes) => SetNotes(notes);

    public void ChangeKind(EntryKind kind) => Kind = kind;

    public void ChangeWallet(Guid walletId) => WalletId = walletId;

    public void ChangeStatus(EntryStatus status) => Status = status;

    /// <summary>Alterna entre Prevista e Recebida/Paga (um toque na lista do mês — FR-006).</summary>
    public void ToggleStatus() =>
        Status = Status == EntryStatus.Planned ? EntryStatus.Settled : EntryStatus.Planned;

    /// <summary>
    /// Altera a data. O Mês de referência acompanha a nova data somente se ainda for igual ao mês da
    /// data antiga, isto é, nunca foi alterado manualmente (RT-04).
    /// </summary>
    public void ChangeDate(DateOnly newDate)
    {
        Rules.Date(newDate, "entryDate");

        if (YearMonth.From(EntryDate).FirstDay == ReferenceMonth)
        {
            ReferenceMonth = YearMonth.From(newDate).FirstDay;
        }

        EntryDate = newDate;
    }

    /// <summary>Define o Mês de referência manualmente (RT-04); aceita qualquer mês dentro do intervalo da RT-03.</summary>
    public void ChangeReferenceMonth(YearMonth month) => SetReferenceMonth(month);

    /// <summary>Move o lançamento para outro mês sem mexer na data (ação "mover para o mês atual" das pendências).</summary>
    public void MoveToMonth(YearMonth month) => SetReferenceMonth(month);

    private void SetDescription(string description)
    {
        Description = Rules.RequiredText(description, Rules.DescriptionMaxLength, "description", "description", "a descrição");
        DescriptionNormalized = TextNormalizer.Normalize(Description);
    }

    private void SetAmount(Money amount) => AmountCents = Rules.EntryAmount(amount);

    private void SetNotes(string? notes) =>
        Notes = Rules.OptionalText(notes, Rules.NotesMaxLength, "notes", "notes", "a observação");

    private void SetReferenceMonth(YearMonth month)
    {
        Rules.Date(month.FirstDay, "referenceMonth");
        ReferenceMonth = month.FirstDay;
    }
}
