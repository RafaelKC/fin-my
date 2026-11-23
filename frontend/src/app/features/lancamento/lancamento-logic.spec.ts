import { novaChaveIdempotencia, situacaoPadrao } from './lancamento-logic';

describe('situacaoPadrao (FR-012)', () => {
  it('data de hoje ou passada → efetivada; futura → prevista', () => {
    expect(situacaoPadrao('2026-03-10', '2026-03-10')).toBe('settled');
    expect(situacaoPadrao('2026-03-09', '2026-03-10')).toBe('settled');
    expect(situacaoPadrao('2026-03-11', '2026-03-10')).toBe('planned');
    expect(situacaoPadrao('2027-01-01', '2026-12-31')).toBe('planned');
  });
});

describe('novaChaveIdempotencia', () => {
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

  it('gera UUID v4 distintos', () => {
    const a = novaChaveIdempotencia();
    expect(a).toMatch(uuid);
    expect(novaChaveIdempotencia()).not.toBe(a);
  });

  it('sem crypto.randomUUID (contexto não seguro) usa getRandomValues', () => {
    const original = crypto.randomUUID;
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true });
    try {
      expect(novaChaveIdempotencia()).toMatch(uuid);
    } finally {
      Object.defineProperty(crypto, 'randomUUID', { value: original, configurable: true });
    }
  });
});
