import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CriadoVia, SeloViaClaude } from './selo-via-claude';

@Component({
  imports: [SeloViaClaude],
  template: `<app-selo-via-claude [via]="via()" />`,
})
class Hospedeiro {
  readonly via = signal<CriadoVia | null | undefined>('claude');
}

describe('SeloViaClaude (008 FR-008)', () => {
  function selo(via: CriadoVia | null | undefined): HTMLElement | null {
    const fixture = TestBed.createComponent(Hospedeiro);
    fixture.componentInstance.via.set(via);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="selo-via-claude"]');
  }

  it('mostra "via Claude" para lançamentos criados pelo Claude', () => {
    expect(selo('claude')?.textContent?.trim()).toBe('via Claude');
  });

  it.each(['app', 'system', null, undefined] as const)('não mostra nada para %s', (via) => {
    expect(selo(via)).toBeNull();
  });
});
