import type { Transfer } from '../mes/month.models';
import type { CardPurchase } from './card-purchase.models';

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
  /** Todas as tags, de acerto ou não (004 research R1); ausente = nenhuma. */
  tagIds?: string[];
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
  /** Todas as tags do lançamento (a de acerto inclusive); `settlementTagId` é a de acerto. */
  tagIds?: string[];
  settlementTagId?: string | null;
  /** Divisão (004 FR-014): grupo das partes e quantas são; `splitCount` ausente/≤ 1 = não dividida. */
  splitGroupId?: string | null;
  splitCount?: number;
}

/** Item de `GET /entries/suggestions` (FR-011, research R8). */
export interface EntrySuggestion {
  description: string;
  uses: number;
  walletId?: string | null;
  /** Só em `kind=card_purchase`: cartão da compra mais recente com a descrição (`null` se arquivado). */
  creditCardId?: string | null;
  /** Tags do lançamento mais recente com a descrição (arquivadas omitidas; FR-003). */
  tagIds?: string[];
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

/** Uma parte de `Split` (contracts/openapi.yaml); `null` = "Eu (sem tag de acerto)". */
export interface SplitPartInput {
  settlementTagId: string | null;
  /** Obrigatório em `mode = amounts`. */
  amount?: string;
}

/** `Split` do contrato: 2 a 10 partes, sem repetir tag de acerto. */
export interface SplitInput {
  mode: 'equal' | 'amounts';
  parts: SplitPartInput[];
}

/** Tipos que a busca devolve (`LedgerItem.type`). */
export type LedgerKind = EntryKind | 'transfer' | 'card_purchase';

/** `LedgerItem` do contrato: `type` diz qual dos três objetos veio preenchido. */
export interface LedgerItem {
  type: LedgerKind;
  entry?: Entry;
  transfer?: Transfer;
  purchase?: CardPurchase;
}

/** Filtros de `GET /entries/search` (FR-025); todos opcionais. */
export interface SearchParams {
  q?: string;
  kinds?: readonly LedgerKind[];
  walletIds?: readonly string[];
  /** Lançamentos com qualquer uma das tags (004 FR-004). */
  tagIds?: readonly string[];
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
