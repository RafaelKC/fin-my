import { LancamentoKind } from '../../core/prefs/device-prefs.service';

export type EntryKind = LancamentoKind;
export type EntryStatus = 'planned' | 'settled';

/** `EntryCreate` do contrato; `amount` é a expressão (RT-05) enviada como digitada. */
export interface EntryCreate {
  kind: EntryKind;
  description: string;
  amount: string;
  date: string;
  walletId: string;
  /** Ausente → o servidor aplica FR-012. */
  status?: EntryStatus;
  /** `AAAA-MM`; ausente → mês da data (RT-04). */
  referenceMonth?: string;
  notes?: string | null;
}

/** `EntryUpdate` do contrato: todos os campos mais a `version` lida. */
export interface EntryUpdate extends Omit<EntryCreate, 'status'> {
  status: EntryStatus;
  version: number;
}

/** `Entry` do contrato (contracts/openapi.yaml). */
export interface Entry {
  id: string;
  kind: EntryKind;
  description: string;
  amountCents: number;
  date: string;
  referenceMonth: string;
  status: EntryStatus;
  walletId: string;
  notes?: string | null;
  createdAt: string;
  version: number;
}

/** Item de `GET /entries/suggestions` (FR-011, research R8). */
export interface EntrySuggestion {
  description: string;
  uses: number;
  walletId?: string | null;
}

export const DESCRICAO_MAX = 80;
export const OBSERVACAO_MAX = 500;
