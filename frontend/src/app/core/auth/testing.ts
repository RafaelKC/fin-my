import { SessionResponse } from './session';

export function sessao(accessToken = 'token-1'): SessionResponse {
  return {
    accessToken,
    accessTokenExpiresAt: '2026-01-01T12:15:00Z',
    user: { id: 'u1', name: 'Ana', email: 'ana@example.com' },
    tenant: { id: 't1', name: 'Espaço da Ana', role: 'owner' },
  };
}

export function problema(status: number, code: string, detail = 'detalhe') {
  return {
    body: { type: 'about:blank', title: code, status, code, detail },
    opts: { status, statusText: code },
  };
}
