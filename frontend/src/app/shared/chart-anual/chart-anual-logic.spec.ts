import { GRAFICO_ALTURA, PontoAnual, calcularGeometria } from './chart-anual-logic';

const pontos: PontoAnual[] = [
  { rotulo: 'JAN', entradasCents: 100000, saidasCents: 40000, saldoCents: 60000 },
  { rotulo: 'FEV', entradasCents: 0, saidasCents: 80000, saldoCents: -20000 },
];

describe('calcularGeometria', () => {
  it('gera um grupo por mês, dentro da área do gráfico', () => {
    const g = calcularGeometria(pontos);
    expect(g.meses).toHaveLength(2);
    for (const m of g.meses) {
      for (const b of [m.entradas, m.saidas]) {
        expect(b.y).toBeGreaterThanOrEqual(0);
        expect(b.y + b.altura).toBeLessThanOrEqual(GRAFICO_ALTURA);
      }
    }
  });

  it('barras proporcionais: a maior ocupa o topo e o zero fica abaixo dela', () => {
    const [jan] = calcularGeometria(pontos).meses;
    expect(jan.entradas.altura).toBeCloseTo(jan.saidas.altura * 2.5, 5);
    expect(jan.entradas.y + jan.entradas.altura).toBeCloseTo(calcularGeometria(pontos).zeroY, 5);
  });

  it('saldo negativo fica abaixo do zero', () => {
    const g = calcularGeometria(pontos);
    expect(g.meses[1].saldo.y).toBeGreaterThan(g.zeroY);
    expect(g.meses[0].saldo.y).toBeLessThan(g.zeroY);
  });

  it('a linha do saldo liga os 12 pontos na ordem', () => {
    const g = calcularGeometria(pontos);
    expect(g.linhaSaldo.split(' ')).toHaveLength(2);
  });

  it('tudo zero não gera NaN', () => {
    const g = calcularGeometria([
      { rotulo: 'JAN', entradasCents: 0, saidasCents: 0, saldoCents: 0 },
    ]);
    expect(g.meses[0].entradas.altura).toBe(0);
    expect(g.linhaSaldo).not.toContain('NaN');
  });
});
