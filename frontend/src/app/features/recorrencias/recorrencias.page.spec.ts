import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problema } from '../../core/auth/testing';
import { carteira } from '../carteiras/testing';
import { cartao } from '../cartoes/testing';
import { RecorrenciasPage } from './recorrencias.page';
import { recorrencia } from './testing';

describe('RecorrenciasPage', () => {
  let http: HttpTestingController;

  function abrir() {
    const fixture = TestBed.createComponent(RecorrenciasPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    http
      .expectOne('/api/v1/credit-cards?archived=all')
      .flush([cartao({ id: 'c-verde', name: 'Verde' })]);
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RecorrenciasPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista valor, dia, carteira/cartão e próxima ocorrência, com os totais mensais (FR-009)', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/recurrences?status=active').flush({
      items: [
        recorrencia(),
        recorrencia({
          id: 'r-prime',
          kind: 'card_purchase',
          description: 'Prime Video',
          amountCents: 1990,
          dayOfMonth: 2,
          walletId: null,
          creditCardId: 'c-verde',
          referenceMonthRule: null,
          nextOccurrenceDate: '2026-07-02',
        }),
      ],
      monthlyIncomeCents: 120000,
      monthlyExpenseCents: 6489,
    });
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="total-entradas"]')!.textContent).toContain(
      'R$ 1.200,00',
    );
    expect(el.querySelector('[data-testid="total-saidas"]')!.textContent).toContain('R$ 64,89');
    const linhas = el.querySelectorAll('[data-testid="recorrencia"]');
    expect(linhas.length).toBe(2);
    expect(linhas[0].textContent).toContain('Tim');
    expect(linhas[0].textContent).toContain('R$ 44,99');
    expect(linhas[0].textContent).toContain('Dia 10');
    expect(linhas[0].textContent).toContain('Azul');
    expect(linhas[0].querySelector('[data-testid="proxima"]')!.textContent).toBe('10/03/2026');
    expect(linhas[0].getAttribute('href')).toBe('/recorrencias/r-tim');
    expect(linhas[1].textContent).toContain('Verde');
    expect(linhas[1].textContent).toContain('Compra no cartão');
    expect(el.querySelector('[data-testid="revisar"]')).toBeNull();
  });

  it('mostra "Revise" quando a carteira ou o cartão foi arquivado', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/recurrences?status=active').flush({
      items: [recorrencia({ needsReview: true, reviewReason: 'wallet_archived' })],
      monthlyIncomeCents: 0,
      monthlyExpenseCents: 4499,
    });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="revisar"]')!.textContent).toBe(
      'Revise: carteira arquivada',
    );
  });

  it('o filtro Encerradas recarrega só as encerradas', () => {
    const { fixture, el } = abrir();
    http
      .expectOne('/api/v1/recurrences?status=active')
      .flush({ items: [], monthlyIncomeCents: 0, monthlyExpenseCents: 0 });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="lista-vazia"]')!.textContent).toContain(
      'Nova recorrência',
    );

    el.querySelector<HTMLButtonElement>('[data-testid="filtro-encerradas"]')!.click();
    http.expectOne('/api/v1/recurrences?status=ended').flush({
      items: [recorrencia({ description: 'WellHub', status: 'ended', nextOccurrenceDate: null })],
      monthlyIncomeCents: 0,
      monthlyExpenseCents: 0,
    });
    fixture.detectChanges();
    const linha = el.querySelector('[data-testid="recorrencia"]')!;
    expect(linha.textContent).toContain('WellHub');
    expect(linha.textContent).toContain('encerrada');
    expect(linha.querySelector('[data-testid="proxima"]')).toBeNull();
  });

  it('erro da API vira aviso com "Tentar de novo"', () => {
    const { fixture, el } = abrir();
    const p = problema(500, 'internal', 'Falhou ao listar.');
    http.expectOne('/api/v1/recurrences?status=active').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-lista"]')!.textContent).toContain(
      'Falhou ao listar.',
    );
    el.querySelector<HTMLButtonElement>('.tentar')!.click();
    http
      .expectOne('/api/v1/recurrences?status=active')
      .flush({ items: [], monthlyIncomeCents: 0, monthlyExpenseCents: 0 });
  });

  it('tem o atalho "Nova recorrência"', () => {
    const { fixture, el } = abrir();
    http
      .expectOne('/api/v1/recurrences?status=active')
      .flush({ items: [], monthlyIncomeCents: 0, monthlyExpenseCents: 0 });
    fixture.detectChanges();
    expect(el.querySelector('a[href="/recorrencias/nova"]')).not.toBeNull();
  });
});
