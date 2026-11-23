import { EntryStatus } from './entry.models';

/** FR-012: data ≤ hoje → Recebida/Paga; data > hoje → Prevista. Datas `AAAA-MM-DD` comparam como texto. */
export function situacaoPadrao(date: string, hoje: string): EntryStatus {
  return date <= hoje ? 'settled' : 'planned';
}

/** Chave de idempotência (UUID v4); `crypto.randomUUID` só existe em contexto seguro. */
export function novaChaveIdempotencia(): string {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** Centavos como expressão de valor aceita pelo servidor: `4499` → `44,99`, `-9990` → `-99,90`. */
export function centavosParaExpressao(cents: number): string {
  const abs = Math.abs(cents);
  const reais = Math.floor(abs / 100);
  return `${cents < 0 ? '-' : ''}${reais},${(abs % 100).toString().padStart(2, '0')}`;
}
