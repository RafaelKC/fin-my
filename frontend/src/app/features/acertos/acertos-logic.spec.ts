import { itensDoNumero, montarCobranca, podeGerarCobranca } from './acertos-logic';
import { SettlementItem } from './settlement.models';
import { linha } from './testing';

describe('acertos-logic', () => {
  it('US3.1: cobrança de março no mês de hoje usa a data de hoje', () => {
    expect(montarCobranca(linha(), '2026-03', '2026-03-20')).toEqual({
      description: 'Bruna — acerto 03/2026',
      amountCents: 2000,
      date: '2026-03-20',
      referenceMonth: '2026-03',
      tagIds: ['t-bruna'],
    });
  });

  it('US3.2: em outro mês a data é o dia 1 daquele mês', () => {
    expect(montarCobranca(linha(), '2026-02', '2026-03-20').date).toBe('2026-02-01');
    expect(montarCobranca(linha(), '2026-04', '2026-03-20').date).toBe('2026-04-01');
  });

  it('US3.3: só gera cobrança com A cobrar > 0', () => {
    expect(podeGerarCobranca(linha({ toChargeCents: 1 }))).toBe(true);
    expect(podeGerarCobranca(linha({ toChargeCents: 0 }))).toBe(false);
    expect(podeGerarCobranca(linha({ toChargeCents: -500 }))).toBe(false);
  });

  it('separa os itens de Gasto, Cobrado e Recebido', () => {
    const item = (parcial: Partial<SettlementItem>): SettlementItem => ({
      group: 'spent',
      type: 'expense',
      id: 'x',
      description: 'x',
      date: '2026-03-10',
      signedCents: 100,
      ...parcial,
    });
    const todos = [
      item({ id: 'a' }),
      item({ id: 'b', group: 'charged', type: 'income', received: false }),
      item({ id: 'c', group: 'charged', type: 'income', received: true }),
    ];
    expect(itensDoNumero(todos, 'spent').map((i) => i.id)).toEqual(['a']);
    expect(itensDoNumero(todos, 'charged').map((i) => i.id)).toEqual(['b', 'c']);
    expect(itensDoNumero(todos, 'received').map((i) => i.id)).toEqual(['c']);
  });
});
