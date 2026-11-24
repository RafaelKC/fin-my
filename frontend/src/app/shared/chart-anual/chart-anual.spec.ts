import { TestBed } from '@angular/core/testing';
import { ChartAnual } from './chart-anual';
import { PontoAnual } from './chart-anual-logic';

const pontos: PontoAnual[] = Array.from({ length: 12 }, (_, i) => ({
  rotulo: `M${i + 1}`,
  entradasCents: 100000,
  saidasCents: 60000,
  saldoCents: 40000 * (i + 1),
}));

describe('ChartAnual', () => {
  it('desenha 12 pares de barras, a linha do saldo e títulos acessíveis (R5)', () => {
    const fixture = TestBed.createComponent(ChartAnual);
    fixture.componentRef.setInput('pontos', pontos);
    fixture.componentRef.setInput('descricao', 'Previsto de 2026');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="grafico-mes"]')).toHaveLength(12);
    expect(el.querySelectorAll('rect.entradas')).toHaveLength(12);
    expect(el.querySelectorAll('rect.saidas')).toHaveLength(12);
    expect(el.querySelector('polyline.saldo')!.getAttribute('points')!.split(' ')).toHaveLength(12);
    expect(el.querySelector('svg')!.getAttribute('aria-label')).toBe('Previsto de 2026');
    expect(el.querySelector('rect.entradas title')!.textContent).toContain('1.000,00');
  });
});
