import { HttpErrorResponse } from '@angular/common/http';

/** `application/problem+json` da API (contracts/openapi.yaml, schema `Problem`). */
export interface Problem {
  type?: string;
  title?: string;
  status?: number;
  /** Código estável (ex.: `token_expired`, `login_locked`). */
  code?: string;
  /** Mensagem pronta para exibir, em pt-BR. */
  detail?: string;
  traceId?: string;
  /** Só em `validation_failed`: mensagens por campo. */
  errors?: Record<string, string[]>;
  /** Só em `login_locked` / `rate_limited`. */
  retryAfterSeconds?: number;
  /** Só em `tag.name_taken` (004): a tag que já tem esse nome. */
  existingTagId?: string;
  /** Só em `tag.settlement_conflict` (004): lançamentos que ficariam com duas tags de acerto. */
  conflicts?: { type: string; id: string; description: string; date: string }[];
}

export const MENSAGEM_ERRO_GENERICO = 'Algo deu errado. Tente novamente.';
export const MENSAGEM_SEM_CONEXAO =
  'Não foi possível falar com o servidor. Verifique sua conexão e tente novamente.';

/** Extrai o ProblemDetails do corpo de um erro HTTP, ou `null` se não for um. */
export function asProblem(error: unknown): Problem | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body: unknown = error.error;
  if (body === null || typeof body !== 'object') return null;
  const problem = body as Problem;
  return typeof problem.code === 'string' || typeof problem.detail === 'string' ? problem : null;
}

/** Mensagem pt-BR para mostrar ao usuário: o `detail` da API quando existir. */
export function problemMessage(error: unknown): string {
  const detail = asProblem(error)?.detail;
  if (typeof detail === 'string' && detail.trim() !== '') return detail;
  if (error instanceof HttpErrorResponse && error.status === 0) return MENSAGEM_SEM_CONEXAO;
  return MENSAGEM_ERRO_GENERICO;
}
