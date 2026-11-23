import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problema } from '../../core/auth/testing';
import { CartoesPage, diaEMes, nomeDoMes } from './cartoes.page';
import { cartao, fatura } from './testing';

describe('formatação da lista de cartões', () => {
  it('nomeia o mês e abrevia a data', () => {
    expect(nomeDoMes('2026-03')).toBe('março');
    expect(diaEMes('2026-03-18')).toBe('18/03');
  });
});

describe('CartoesPage', () => {
  let http: HttpTestingController;

  function abrir() {
    const fixture = TestBed.createComponent(CartoesPage);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CartoesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista o cartão com a fatura atual zerada e a data de fechamento (US1.1)', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/credit-cards?archived=false').flush([cartao()]);
    fixture.detectChanges();

    const linha = el.querySelector('[data-testid="cartao"]')!;
    expect(linha.textContent).toContain('Azul');
    const atual = linha.querySelector('[data-testid="fatura-atual"]')!;
    expect(atual.textContent).toContain('Fatura de março');
    expect(atual.textContent).toContain('R$ 0,00');
    expect(atual.textContent).toContain('fecha em 18/03');
    expect(linha.querySelector('[data-testid="proxima-fatura"]')!.textContent).toContain('abril');
    expect(linha.querySelector<HTMLElement>('.cor')!.style.background).toContain('30, 136, 229');
    expect(linha.querySelector('a')!.getAttribute('href')).toBe('/cartoes/c-azul/editar');
  });

  it('mostra limite disponível quando há limite; vermelho se negativo (US6.1, FR-026)', () => {
    const { fixture, el } = abrir();
    http
      .expectOne('/api/v1/credit-cards?archived=false')
      .flush([
        cartao({ limitCents: 300000, availableLimitCents: 233334 }),
        cartao({ id: 'c-verde', name: 'Verde', limitCents: 10000, availableLimitCents: -2500 }),
      ]);
    fixture.detectChanges();

    const limites = el.querySelectorAll('[data-testid="limite-disponivel"]');
    expect(limites[0].textContent).toContain('R$ 2.333,34');
    expect(limites[0].classList.contains('negativo')).toBe(false);
    expect(limites[1].textContent).toContain('-R$ 25,00');
    expect(limites[1].classList.contains('negativo')).toBe(true);
  });

  it('sem limite cadastrado não mostra limite disponível (US6.3)', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/credit-cards?archived=false').flush([cartao()]);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="limite-disponivel"]')).toBeNull();
  });

  it('faturas em aberto aparecem; vencida em vermelho (FR-025)', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/credit-cards?archived=false').flush([
      cartao({
        openPastInvoices: [
          fatura({
            dueMonth: '2026-01',
            dueDate: '2026-01-25',
            status: 'overdue',
            totalCents: 15000,
            remainingCents: 15000,
          }),
          fatura({
            dueMonth: '2026-02',
            dueDate: '2026-02-25',
            status: 'closed',
            totalCents: 8000,
            remainingCents: 3000,
          }),
        ],
      }),
    ]);
    fixture.detectChanges();

    const abertas = el.querySelectorAll('[data-testid="fatura-aberta"]');
    expect(abertas.length).toBe(2);
    expect(abertas[0].textContent).toContain('janeiro');
    expect(abertas[0].textContent).toContain('R$ 150,00 em aberto');
    expect(abertas[0].textContent).toContain('venceu em 25/01');
    expect(abertas[0].classList.contains('vencida')).toBe(true);
    expect(abertas[1].textContent).toContain('R$ 30,00 em aberto');
    expect(abertas[1].textContent).toContain('vence em 25/02');
    expect(abertas[1].classList.contains('vencida')).toBe(false);
  });

  it('sem cartões, convida a criar o primeiro', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/credit-cards?archived=false').flush([]);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="lista-vazia"]')!.textContent).toContain('Novo cartão');
    expect(el.querySelector('a[href="/cartoes/novo"]')).not.toBeNull();
  });

  it('filtro Arquivados recarrega só os arquivados', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/credit-cards?archived=false').flush([cartao()]);
    el.querySelector<HTMLInputElement>('[data-testid="filtro-arquivados"]')!.click();
    fixture.detectChanges();
    http
      .expectOne('/api/v1/credit-cards?archived=true')
      .flush([cartao({ id: 'c-velho', name: 'Velho', archived: true })]);
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="cartao"]').length).toBe(1);
    expect(el.textContent).toContain('Arquivado');
  });

  it('mostra o erro da API', () => {
    const { fixture, el } = abrir();
    const p = problema(500, 'server_error', 'Falhou.');
    http.expectOne('/api/v1/credit-cards?archived=false').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-lista"]')!.textContent).toContain('Falhou.');
  });
});
