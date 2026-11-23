import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problema } from '../../core/auth/testing';
import { CarteirasPage } from './carteiras.page';
import { carteira } from './testing';

describe('CarteirasPage', () => {
  let http: HttpTestingController;

  function abrir() {
    const fixture = TestBed.createComponent(CarteirasPage);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CarteirasPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista carteiras com saldo, cor e a principal marcada (US1.1)', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/wallets?archived=false').flush([
      carteira(),
      carteira({
        id: 'w-verde',
        name: 'Verde',
        color: '#43A047',
        isPrimary: false,
        currentBalanceCents: -1500,
      }),
    ]);
    fixture.detectChanges();

    const linhas = el.querySelectorAll('[data-testid="carteira"]');
    expect(linhas.length).toBe(2);
    expect(linhas[0].textContent).toContain('Azul');
    expect(linhas[0].textContent).toContain('R$ 500,00');
    expect(linhas[0].querySelector('[data-testid="principal"]')).not.toBeNull();
    expect(linhas[1].querySelector('[data-testid="principal"]')).toBeNull();
    expect(linhas[1].textContent).toContain('-R$ 15,00');
    expect(linhas[1].querySelector<HTMLElement>('.cor')!.style.background).toContain('67, 160, 71');
    expect(linhas[0].getAttribute('href')).toBe('/carteiras/w-azul');
  });

  it('sem carteiras, convida a criar a primeira', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/wallets?archived=false').flush([]);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="lista-vazia"]')!.textContent).toContain('Nova carteira');
    expect(el.querySelector('a[href="/carteiras/nova"]')).not.toBeNull();
  });

  it('filtro Arquivadas recarrega só as arquivadas', () => {
    const { fixture, el } = abrir();
    http.expectOne('/api/v1/wallets?archived=false').flush([carteira()]);
    el.querySelector<HTMLInputElement>('[data-testid="filtro-arquivadas"]')!.click();
    fixture.detectChanges();
    http
      .expectOne('/api/v1/wallets?archived=true')
      .flush([carteira({ id: 'w-velha', name: 'Velha', archived: true, isPrimary: false })]);
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="carteira"]').length).toBe(1);
    expect(el.textContent).toContain('Arquivada');
  });

  it('mostra erro da API', () => {
    const { fixture, el } = abrir();
    const p = problema(500, 'server_error', 'Falhou.');
    http.expectOne('/api/v1/wallets?archived=false').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-lista"]')!.textContent).toContain('Falhou.');
  });
});
