import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { formatBrl } from '../../core/money';
import { PontoAnual, calcularGeometria } from './chart-anual-logic';

/**
 * Gráfico do Painel (006 research R5, Princípio V): barras Entradas × Saídas por mês e a linha do
 * Saldo final, em SVG próprio, com as cores do tema. Cada marca tem `<title>` para leitores de tela.
 */
@Component({
  selector: 'app-chart-anual',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './chart-anual.html',
  styleUrl: './chart-anual.scss',
})
export class ChartAnual {
  readonly pontos = input.required<readonly PontoAnual[]>();
  /** Texto acessível do gráfico inteiro (ex.: "Previsto de 2026"). */
  readonly descricao = input('Entradas e saídas por mês e saldo final');

  protected readonly geometria = computed(() => calcularGeometria(this.pontos()));
  protected readonly dados = computed(() => {
    const pontos = this.pontos();
    return this.geometria().meses.map((m, i) => ({ ...m, ponto: pontos[i] }));
  });

  protected readonly brl = formatBrl;
}
