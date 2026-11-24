import { Entry } from '../lancamento/entry.models';
import {
  alternarOrdem,
  direcaoDoDeslize,
  estaAtrasada,
  filtrarPorSituacao,
  formatarData,
  lerCarteiras,
  lerSituacao,
  lerTags,
  montarQuery,
  ordenar,
} from './mes-logic';

function e(p: Partial<Entry>): Entry {
  return {
    id: 'x',
    kind: 'expense',
    description: 'Tim',
    amountCents: 100,
    date: '2026-03-10',
    referenceMonth: '2026-03',
    status: 'planned',
    walletId: 'a',
    createdAt: '',
    version: 1,
    ...p,
  };
}

describe('mes-logic', () => {
  it('lê e monta a query string', () => {
    expect(lerCarteiras('a, b,a,')).toEqual(['a', 'b']);
    expect(lerCarteiras(null)).toEqual([]);
    expect(lerSituacao('planned')).toBe('planned');
    expect(lerSituacao('x')).toBeNull();
    expect(montarQuery(['a', 'b'], 'settled')).toEqual({ carteiras: 'a,b', situacao: 'settled' });
    expect(montarQuery([], null)).toEqual({ carteiras: null, situacao: null });
    expect(montarQuery([], null, ['t1', 't2'])).toEqual({
      carteiras: null,
      situacao: null,
      tags: 't1,t2',
    });
    expect(lerTags('t1, t2,t1,')).toEqual(['t1', 't2']);
    expect(lerTags(null)).toEqual([]);
  });

  it('filtra por situação e marca atrasadas', () => {
    const lista = [e({ id: '1' }), e({ id: '2', status: 'settled' })];
    expect(filtrarPorSituacao(lista, 'settled').map((x) => x.id)).toEqual(['2']);
    expect(filtrarPorSituacao(lista, null)).toHaveLength(2);
    expect(estaAtrasada(e({}), '2026-03-11')).toBe(true);
    expect(estaAtrasada(e({}), '2026-03-10')).toBe(false);
    expect(estaAtrasada(e({ status: 'settled' }), '2026-04-01')).toBe(false);
  });

  it('ordena por coluna sem diferenciar acento e inverte', () => {
    const lista = [
      e({ id: '1', description: 'Zé', amountCents: 5 }),
      e({ id: '2', description: 'ágata', amountCents: 9 }),
    ];
    const nome = () => '';
    expect(
      ordenar(lista, { coluna: 'description', direcao: 'asc' }, nome).map((x) => x.id),
    ).toEqual(['2', '1']);
    expect(ordenar(lista, { coluna: 'amount', direcao: 'desc' }, nome).map((x) => x.id)).toEqual([
      '2',
      '1',
    ]);
    expect(ordenar(lista, null, nome).map((x) => x.id)).toEqual(['1', '2']);
    expect(alternarOrdem(null, 'date')).toEqual({ coluna: 'date', direcao: 'asc' });
    expect(alternarOrdem({ coluna: 'date', direcao: 'asc' }, 'date').direcao).toBe('desc');
  });

  it('interpreta deslizes e formata datas', () => {
    expect(direcaoDoDeslize(-100, 5)).toBe(1);
    expect(direcaoDoDeslize(100, 5)).toBe(-1);
    expect(direcaoDoDeslize(30, 0)).toBe(0);
    expect(direcaoDoDeslize(80, 80)).toBe(0);
    expect(formatarData('2026-03-25')).toBe('25/03/2026');
  });
});
