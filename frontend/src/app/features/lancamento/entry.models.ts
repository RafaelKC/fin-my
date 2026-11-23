import type { Transfer } from '../mes/month.models';

export type EntryKind = 'expense' | 'income';
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

/** `TransferInput` do contrato; `description` vazia/ausente → "Transferência" no servidor. */
export interface TransferCreate {
  fromWalletId: string;
  toWalletId: string;
  amount: string;
  date: string;
  description?: string;
  notes?: string | null;
}

export const DESCRICAO_TRANSFERENCIA = 'Transferência';

/** Tipos que a busca devolve (`LedgerItem.type`). */
export type LedgerKind = EntryKind | 'transfer';

/** `LedgerItem` do contrato: `type` diz qual dos dois objetos veio preenchido. */
export interface LedgerItem {
  type: LedgerKind;
  entry?: Entry;
  transfer?: Transfer;
}

/** Filtros de `GET /entries/search` (FR-025); todos opcionais. */
export interface SearchParams {
  q?: string;
  kinds?: readonly LedgerKind[];
  walletIds?: readonly string[];
  status?: EntryStatus | null;
  from?: string;
  to?: string;
  cursor?: string | null;
  limit?: number;
}

/** Resposta da busca; `totalCents` e `totalCount` valem para TODOS os resultados. */
export interface SearchResult {
  items: LedgerItem[];
  totalCents: number;
  totalCount: number;
  nextCursor?: string | null;
}

/** `DeletionResult` do contrato. */
export interface DeletionResult {
  deletionBatchId: string;
}
