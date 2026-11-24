import { buscarTags, chipsFrequentes, ordenarPorUso, tagComNome } from './tag-picker-logic';
import { tag } from './testing';

describe('tag-picker-logic', () => {
  const tags = [
    tag({ id: '1', name: 'Uber', uses90d: 3 }),
    tag({ id: '2', name: 'Mercado', uses90d: 10 }),
    tag({ id: '3', name: 'Casa', uses90d: 3 }),
    tag({ id: '4', name: 'Farmácia', uses90d: 0, archived: true }),
  ];

  it('ordena por usos nos últimos 90 dias e, no empate, pelo nome (research R2)', () => {
    expect(ordenarPorUso(tags).map((t) => t.name)).toEqual(['Mercado', 'Casa', 'Uber', 'Farmácia']);
  });

  it('as chips são as 8 ativas mais usadas', () => {
    const muitas = Array.from({ length: 12 }, (_, i) =>
      tag({ id: `t${i}`, name: `Tag ${String(i).padStart(2, '0')}`, uses90d: i }),
    );
    const chips = chipsFrequentes([
      ...muitas,
      tag({ id: 'x', name: 'Velha', archived: true, uses90d: 99 }),
    ]);
    expect(chips).toHaveLength(8);
    expect(chips[0].name).toBe('Tag 11');
    expect(chips.some((t) => t.archived)).toBe(false);
  });

  it('acha a tag pelo nome sem diferenciar caixa nem acentos (US1.3)', () => {
    expect(tagComNome(tags, ' mercado ')?.id).toBe('2');
    expect(tagComNome(tags, 'FARMACIA')?.id).toBe('4');
    expect(tagComNome(tags, 'Mercad')).toBeNull();
    expect(tagComNome(tags, '  ')).toBeNull();
  });

  it('busca ativas que contêm o texto, fora as já escolhidas e as arquivadas', () => {
    expect(buscarTags(tags, 'ca', []).map((t) => t.name)).toEqual(['Mercado', 'Casa']);
    expect(buscarTags(tags, 'ca', ['2']).map((t) => t.name)).toEqual(['Casa']);
    expect(buscarTags(tags, '', [])).toEqual([]);
  });
});
