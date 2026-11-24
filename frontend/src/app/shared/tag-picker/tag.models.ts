/** `Tag` do contrato (004 contracts/openapi.yaml). */
export interface Tag {
  id: string;
  name: string;
  color: string;
  isSettlement: boolean;
  archived: boolean;
  /** Usos nos últimos 90 dias (research R2): base da ordem das chips. */
  uses90d: number;
  inUse: boolean;
  version: number;
}

/** Corpo de `POST /tags`. */
export interface TagInput {
  name: string;
  color?: string;
  isSettlement?: boolean;
}

/** Tags mostradas como botões de um toque (research R2). */
export const CHIPS_FREQUENTES = 8;
/** Máximo de tags por lançamento (research R1). */
export const TAGS_MAX = 20;
/** Tamanho máximo do nome (FR-001). */
export const TAG_NOME_MAX = 30;

export const MENSAGEM_DUAS_DE_ACERTO = 'Só uma tag de acerto por lançamento. Usar Dividir?';
