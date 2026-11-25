import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SeloRecorrencia } from './selo-recorrencia';

@Component({
  imports: [SeloRecorrencia],
  template: `<app-selo-recorrencia [recorrenciaId]="id" [alterada]="alterada" />`,
})
class Hospedeiro {
  id: string | null | undefined = 'r-tim';
  alterada: boolean | undefined = false;
}

describe('SeloRecorrencia', () => {
  function abrir(id: string | null | undefined, alterada?: boolean) {
    const fixture = TestBed.createComponent(Hospedeiro);
    fixture.componentInstance.id = id;
    fixture.componentInstance.alterada = alterada;
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('mostra o ícone de recorrência numa ocorrência', () => {
    const el = abrir('r-tim');
    expect(el.querySelector('[data-testid="selo-recorrencia"]')).not.toBeNull();
    expect(el.querySelector('app-icone')!.getAttribute('aria-label')).toBe('Recorrência');
    expect(el.querySelector('[data-testid="selo-alterada"]')).toBeNull();
  });

  it('acrescenta "alterada" quando a ocorrência foi editada à mão', () => {
    const el = abrir('r-tim', true);
    expect(el.querySelector('[data-testid="selo-alterada"]')!.textContent).toBe('alterada');
  });

  it('não mostra nada num lançamento comum', () => {
    expect(abrir(null).querySelector('[data-testid="selo-recorrencia"]')).toBeNull();
    expect(abrir(undefined).querySelector('[data-testid="selo-recorrencia"]')).toBeNull();
  });
});
