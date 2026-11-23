import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { sessao } from '../core/auth/testing';
import { LancamentoLauncher } from '../features/lancamento/lancamento.launcher';
import { Shell } from './shell';

describe('Shell', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(AuthService).startSession(sessao());
  });

  it('mostra o nome do Espaço no topo', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="nome-do-espaco"]')?.textContent).toContain(
      'Espaço da Ana',
    );
  });

  it('tem navegação principal com o item "Mês"', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const nav = el.querySelector('nav[aria-label="Principal"]');
    expect(nav?.textContent).toContain('Mês');
    expect(nav?.querySelector('a')?.getAttribute('href')).toBe('/');
    expect(el.querySelector('main')).not.toBeNull();
  });

  it('tem o item "Carteiras" levando a /carteiras', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const link = (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Principal"] a[href="/carteiras"]',
    );
    expect(link?.textContent).toContain('Carteiras');
  });

  it('tem o "+" Lançar na barra, que abre o formulário rápido (FR-008)', () => {
    const abrir = vi
      .spyOn(TestBed.inject(LancamentoLauncher), 'abrir')
      .mockReturnValue(undefined as never);
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const botao = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'nav[aria-label="Principal"] button[data-testid="lancar"]',
    );
    expect(botao?.textContent).toContain('Lançar');
    botao!.click();
    expect(abrir).toHaveBeenCalledOnce();
  });
});
