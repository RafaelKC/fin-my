namespace FinMy.Domain.Entries;

/// <summary>Situação: Prevista (<see cref="Planned"/>) ou Recebida/Paga (<see cref="Settled"/>) — 002 FR-006.</summary>
public enum EntryStatus
{
    Planned,
    Settled,
}
