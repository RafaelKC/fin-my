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
    expect(nav?.querySelector('a')?.getAttribute('href')).toBe('/mes');
    expect(el.querySelector('main')).not.toBeNull();
  });

  it('tem o item "Cartões" levando a /cartoes, logo depois de "Mês"', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const nav = (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Principal"]',
    )!;
    const links = Array.from(nav.querySelectorAll(':scope > a'));
    expect(links[1].getAttribute('href')).toBe('/cartoes');
    expect(links[1].textContent).toContain('Cartões');
  });

  it('"Carteiras" e "Perfil" ficam em "Mais" (painel fechado até tocar)', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const nav = el.querySelector('nav[aria-label="Principal"]')!;
    // fora da barra principal: só dentro do painel de "Mais"
    expect(nav.querySelector(':scope > a[href="/carteiras"]')).toBeNull();
    const painel = nav.querySelector('#mais-painel')!;
    expect(painel.querySelector('a[href="/carteiras"]')?.textContent).toContain('Carteiras');
    expect(painel.querySelector('a[href="/busca"]')?.textContent).toContain('Busca');
    expect(painel.querySelector('a[href="/tags"]')?.textContent).toContain('Tags');
    expect(painel.querySelector('a[href="/perfil"]')?.textContent).toContain('Perfil');

    const mais = nav.querySelector<HTMLButtonElement>('[data-testid="mais"]')!;
    expect(mais.textContent).toContain('Mais');
    expect(mais.getAttribute('aria-expanded')).toBe('false');
    mais.click();
    fixture.detectChanges();
    expect(mais.getAttribute('aria-expanded')).toBe('true');
    expect(nav.querySelector('.mais.aberto')).not.toBeNull();
  });

  it('a barra segue Mês · Cartões · + · Acertos · Mais', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const nav = (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Principal"]',
    )!;
    const ordem = Array.from(nav.children).map((c) =>
      ['Mês', 'Cartões', 'Lançar', 'Acertos', 'Mais'].find((t) => c.textContent?.includes(t)),
    );
    expect(ordem).toEqual(['Mês', 'Cartões', 'Lançar', 'Acertos', 'Mais']);
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

  it('tem o item "Acertos" levando a /acertos, depois do "+"', () => {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const nav = (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Principal"]',
    )!;
    const link = nav.querySelector(':scope > a[href="/acertos"]');
    expect(link?.textContent).toContain('Acertos');
    const itens = Array.from(nav.children).map((c) => c.textContent?.trim());
    expect(itens.findIndex((t) => t?.includes('Lançar'))).toBeLessThan(
      itens.findIndex((t) => t?.includes('Acertos')),
    );
  });
});
