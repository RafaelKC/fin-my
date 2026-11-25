import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { NGX_ECHARTS_CONFIG } from 'ngx-echarts';
import {
  ZardChartImports,
  type ZardChartConfig,
  type ZardChartSeries,
} from '@/shared/components/chart';
import { formatBrl } from '../../core/money';
import { PontoAnual } from './chart-anual-logic';

/** "R$ 12 mil" para os eixos; os valores exatos ficam na dica e na lista acessível. */
function compacto(reais: number): string {
  const abs = Math.abs(reais);
  if (abs >= 1000) {
    const mil = reais / 1000;
    return `R$ ${mil.toLocaleString('pt-BR', { maximumFractionDigits: abs >= 10000 ? 0 : 1 })} mil`;
  }
  return `R$ ${reais.toLocaleString('pt-BR', { maximumFractionDigits: 0 })}`;
}

/**
 * Gráfico do Painel (006 research R5, Princípio V): barras Entradas × Saídas por mês e a linha do
 * Saldo final, com o chart do Zard UI (ECharts) nas cores do tema. A lista escondida ao lado
 * descreve os 12 meses para leitores de tela; sem o ECharts configurado, ela aparece no lugar.
 */
@Component({
  selector: 'app-chart-anual',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...ZardChartImports],
  templateUrl: './chart-anual.html',
  styleUrl: './chart-anual.scss',
})
export class ChartAnual {
  readonly pontos = input.required<readonly PontoAnual[]>();
  /** Texto acessível do gráfico inteiro (ex.: "Previsto de 2026"). */
  readonly descricao = input('Entradas e saídas por mês e saldo final');

  /** O ECharts é registrado no app (provideZardCharts); nos testes de unidade não está. */
  protected readonly comGrafico = inject(NGX_ECHARTS_CONFIG, { optional: true }) !== null;

  protected readonly config: ZardChartConfig = {
    entradas: { label: 'Entradas', color: 'var(--chart-1)' },
    saidas: { label: 'Saídas', color: 'var(--chart-2)' },
    saldo: { label: 'Saldo final', color: 'var(--chart-3)' },
  };

  protected readonly series: ZardChartSeries[] = [
    { dataKey: 'entradas', type: 'bar', radius: [6, 6, 0, 0] },
    { dataKey: 'saidas', type: 'bar', radius: [6, 6, 0, 0] },
    {
      dataKey: 'saldo',
      type: 'line',
      smooth: 0.3,
      strokeWidth: 3,
      symbolSize: 7,
      showSymbol: true,
    },
  ];

  protected readonly dados = computed(() =>
    this.pontos().map((p) => ({
      mes: p.rotulo,
      entradas: p.entradasCents / 100,
      saidas: p.saidasCents / 100,
      saldo: p.saldoCents / 100,
    })),
  );

  protected readonly brl = formatBrl;
  protected readonly eixo = compacto;
  protected readonly dica = (valor: number) => formatBrl(Math.round(valor * 100));
}
