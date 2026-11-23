/** Ações em lote de `POST /entries/bulk` (research R9). */
export type AcaoPendencia = 'settle' | 'move_to_current_month' | 'delete';

/** Resposta de `POST /entries/bulk`; `deletionBatchId` só vem em `delete`. */
export interface ResultadoLote {
  affected: number;
  deletionBatchId: string | null;
}

/** Máximo de ids por chamada (contracts/openapi.yaml). */
export const LOTE_MAXIMO = 200;
