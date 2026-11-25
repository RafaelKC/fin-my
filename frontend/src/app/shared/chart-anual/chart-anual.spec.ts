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
  it('descreve os 12 meses com os valores em R$ e o rótulo acessível (R5)', () => {
    const fixture = TestBed.createComponent(ChartAnual);
    fixture.componentRef.setInput('pontos', pontos);
    fixture.componentRef.setInput('descricao', 'Previsto de 2026');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const meses = el.querySelectorAll('[data-testid="grafico-mes"]');
    expect(meses).toHaveLength(12);
    expect(meses[0].textContent).toContain('1.000,00');
    expect(meses[0].textContent).toContain('600,00');
    expect(meses[11].textContent).toContain('4.800,00');
    expect(el.querySelector('[data-testid="grafico"]')!.getAttribute('aria-label')).toBe(
      'Previsto de 2026',
    );
  });
});
