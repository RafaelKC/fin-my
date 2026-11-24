/** Um mês do gráfico anual: Entradas, Saídas e Saldo final em centavos. */
export interface PontoAnual {
  rotulo: string;
  entradasCents: number;
  saidasCents: number;
  saldoCents: number;
}

export interface BarraAnual {
  x: number;
  y: number;
  largura: number;
  altura: number;
}

export interface MesDoGrafico {
  rotulo: string;
  centroX: number;
  entradas: BarraAnual;
  saidas: BarraAnual;
  saldo: { x: number; y: number };
}

export interface GeometriaAnual {
  largura: number;
  altura: number;
  /** Altura (y) do zero. */
  zeroY: number;
  meses: MesDoGrafico[];
  /** `points` da polilinha do Saldo final. */
  linhaSaldo: string;
}

export const GRAFICO_LARGURA = 720;
export const GRAFICO_ALTURA = 280;
const MARGEM_SUPERIOR = 12;
const MARGEM_INFERIOR = 28;
const MARGEM_LATERAL = 8;

/**
 * Posiciona barras (Entradas × Saídas) e a linha do Saldo final num plano SVG (research R5). A escala
 * vai do menor ao maior valor entre barras e saldo, sempre incluindo o zero; só números, sem DOM.
 */
export function calcularGeometria(pontos: readonly PontoAnual[]): GeometriaAnual {
  const valores = pontos.flatMap((p) => [p.entradasCents, p.saidasCents, p.saldoCents]);
  const maximo = Math.max(0, ...valores);
  const minimo = Math.min(0, ...valores);
  const amplitude = maximo - minimo || 1;
  const area = GRAFICO_ALTURA - MARGEM_SUPERIOR - MARGEM_INFERIOR;
  const y = (cents: number): number => MARGEM_SUPERIOR + ((maximo - cents) / amplitude) * area;
  const zeroY = y(0);

  const faixa = (GRAFICO_LARGURA - 2 * MARGEM_LATERAL) / Math.max(pontos.length, 1);
  const largura = faixa * 0.32;

  const barra = (x: number, cents: number): BarraAnual => {
    const topo = y(cents);
    return { x, y: Math.min(topo, zeroY), largura, altura: Math.abs(zeroY - topo) };
  };

  const meses = pontos.map((p, i): MesDoGrafico => {
    const centroX = MARGEM_LATERAL + faixa * (i + 0.5);
    return {
      rotulo: p.rotulo,
      centroX,
      entradas: barra(centroX - largura - 1, p.entradasCents),
      saidas: barra(centroX + 1, p.saidasCents),
      saldo: { x: centroX, y: y(p.saldoCents) },
    };
  });

  return {
    largura: GRAFICO_LARGURA,
    altura: GRAFICO_ALTURA,
    zeroY,
    meses,
    linhaSaldo: meses.map((m) => `${round(m.saldo.x)},${round(m.saldo.y)}`).join(' '),
  };
}

function round(valor: number): number {
  return Math.round(valor * 100) / 100;
}
