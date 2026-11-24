import { calcularDivisao, montarSplit, partesIniciais, totalDividido } from './dividir-logic';

const eu = { tagId: null, valor: '' };
const bruna = { tagId: 't-bruna', valor: '' };
const carla = { tagId: 't-carla', valor: '' };

describe('dividir-logic (004 US4)', () => {
  it('partes iguais: R$ 239,80 entre Eu e Bruna → 119,90 cada (US4.1)', () => {
    const r = calcularDivisao('equal', 23980, [eu, bruna]);
    expect(r.valores).toEqual([11990, 11990]);
    expect(r.erro).toBeNull();
  });

  it('partes iguais: R$ 100,00 em 3 → 33,34 / 33,33 / 33,33 na ordem listada (US4.4)', () => {
    const r = calcularDivisao('equal', 10000, [eu, bruna, carla]);
    expect(r.valores).toEqual([3334, 3333, 3333]);
  });

  it('valores informados que fecham o total não têm aviso (US4.2)', () => {
    const r = calcularDivisao('amounts', 20790, [
      { tagId: null, valor: '110,94' },
      { tagId: 't-carla', valor: '11,99' },
      { tagId: 't-bruna', valor: '84,97' },
    ]);
    expect(r.valores).toEqual([11094, 1199, 8497]);
    expect(r.diferenca).toBe(0);
    expect(r.erro).toBeNull();
  });

  it('valores informados aceitam expressão (RT-05)', () => {
    const r = calcularDivisao('amounts', 1000, [
      { tagId: null, valor: '5+2,50' },
      { tagId: 't-bruna', valor: '2,50' },
    ]);
    expect(r.erro).toBeNull();
  });

  it('soma menor → "Faltam R$ X" e bloqueia (US4.3)', () => {
    const r = calcularDivisao('amounts', 10000, [
      { tagId: null, valor: '40' },
      { tagId: 't-bruna', valor: '50' },
    ]);
    expect(r.aviso).toBe('Faltam R$ 10,00');
    expect(r.erro).toBe('Faltam R$ 10,00');
    expect(r.diferenca).toBe(1000);
  });

  it('soma maior → "Sobram R$ X" e bloqueia (US4.3)', () => {
    const r = calcularDivisao('amounts', 10000, [
      { tagId: null, valor: '60' },
      { tagId: 't-bruna', valor: '50,01' },
    ]);
    expect(r.aviso).toBe('Sobram R$ 10,01');
    expect(r.erro).not.toBeNull();
  });

  it('valor em branco ou inválido bloqueia', () => {
    const r = calcularDivisao('amounts', 10000, [
      { tagId: null, valor: '' },
      { tagId: 't-bruna', valor: 'abc' },
    ]);
    expect(r.erro).toBe('Informe o valor de cada parte.');
  });

  it('recusa menos de 2 e mais de 10 partes', () => {
    expect(calcularDivisao('equal', 100, [eu]).erro).not.toBeNull();
    const onze = Array.from({ length: 11 }, (_, i) => ({ tagId: `t${i}`, valor: '' }));
    expect(calcularDivisao('equal', 100000, onze).erro).not.toBeNull();
  });

  it('recusa tag repetida, "Eu" repetido e total inválido', () => {
    expect(calcularDivisao('equal', 100, [bruna, bruna]).erro).not.toBeNull();
    expect(calcularDivisao('equal', 100, [eu, eu]).erro).not.toBeNull();
    expect(calcularDivisao('equal', null, [eu, bruna]).erro).toBe('Informe o valor total.');
    expect(calcularDivisao('equal', 1, [eu, bruna, carla]).erro).not.toBeNull();
  });

  it('total: valor da parcela × N; total digitado fica como está', () => {
    expect(totalDividido(2500, 4, 'installment_amount')).toBe(10000);
    expect(totalDividido(10000, 4, 'total_amount')).toBe(10000);
    expect(totalDividido(10000, 1, 'installment_amount')).toBe(10000);
  });

  it('partes iniciais: Eu e a tag de acerto já escolhida', () => {
    expect(partesIniciais('t-bruna')).toEqual([
      { tagId: null, valor: '' },
      { tagId: 't-bruna', valor: '' },
    ]);
  });

  it('corpo split: equal sem amount; amounts com o valor digitado', () => {
    expect(montarSplit('equal', [eu, bruna])).toEqual({
      mode: 'equal',
      parts: [{ settlementTagId: null }, { settlementTagId: 't-bruna' }],
    });
    expect(
      montarSplit('amounts', [
        { tagId: null, valor: ' 60 ' },
        { tagId: 't-bruna', valor: '40' },
      ]),
    ).toEqual({
      mode: 'amounts',
      parts: [
        { settlementTagId: null, amount: '60' },
        { settlementTagId: 't-bruna', amount: '40' },
      ],
    });
  });
});
