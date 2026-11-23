import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { problema } from '../../core/auth/testing';
import { today } from '../../core/money';
import { CarteiraFormPage, centavosParaCampo } from './carteira-form.page';
import { carteira } from './testing';

describe('centavosParaCampo', () => {
  it('formata para o campo de saldo', () => {
    expect(centavosParaCampo(50000)).toBe('500,00');
    expect(centavosParaCampo(-15000)).toBe('-150,00');
    expect(centavosParaCampo(123456)).toBe('1234,56');
    expect(centavosParaCampo(0)).toBe('0,00');
  });
});

describe('CarteiraFormPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<CarteiraFormPage>;
  let el: HTMLElement;
  let navegar: ReturnType<typeof vi.spyOn>;

  function abrir(id: string | null) {
    TestBed.configureTestingModule({
      imports: [CarteiraFormPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(CarteiraFormPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }
  const preencher = (id: string, valor: string) => {
    const campo = el.querySelector<HTMLInputElement>(id)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const clicar = (texto: string) => {
    const botao = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === texto,
    )!;
    botao.click();
    fixture.detectChanges();
  };
  const estabilizar = async () => {
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
    // o diálogo só emite o fechamento depois da animação de saída (150 ms)
    await new Promise((r) => setTimeout(r, 250));
    TestBed.inject(ApplicationRef).tick();
  };
  const dialogo = () => TestBed.inject(OverlayContainer).getContainerElement();
  const existente = (parcial = {}) => {
    abrir('w-azul');
    http.expectOne('/api/v1/wallets/w-azul').flush(carteira(parcial));
    fixture.detectChanges();
  };

  afterEach(() => http.verify());

  it('nova: 10 cores, saldo e data padrão, prévia da expressão (RT-05)', () => {
    abrir(null);
    expect(el.querySelectorAll('.amostra').length).toBe(10);
    expect(el.querySelector<HTMLInputElement>('#data-saldo')!.value).toBe(today());
    preencher('#saldo', '400+100,50');
    expect(el.querySelector('[data-testid="saldo-previa"]')!.textContent).toBe('= R$ 500,50');
    preencher('#saldo', '-150');
    expect(el.querySelector('[data-testid="saldo-previa"]')!.textContent).toBe('= -R$ 150,00');
    preencher('#saldo', '2+');
    expect(el.querySelector('[data-testid="saldo-previa"]')!.textContent).toContain(
      'Valor inválido',
    );
  });

  it('nova: cria enviando a expressão como digitada e volta para a lista (US1.1)', () => {
    abrir(null);
    preencher('#nome', ' Azul ');
    preencher('#saldo', '500');
    preencher('#data-saldo', '2026-03-01');
    (el.querySelectorAll('.amostra')[2] as HTMLElement).click();
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

    const req = http.expectOne('/api/v1/wallets');
    expect(req.request.body).toEqual({
      name: 'Azul',
      type: 'checking',
      color: '#E53935',
      initialBalance: '500',
      initialBalanceDate: '2026-03-01',
    });
    req.flush(carteira(), { status: 201, statusText: 'Created' });
    expect(navegar).toHaveBeenCalledWith('/carteiras');
  });

  it('nome vazio ou expressão inválida não chama a API', () => {
    abrir(null);
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
    expect(el.textContent).toContain('Informe o nome da carteira.');
    preencher('#nome', 'Azul');
    preencher('#saldo', 'abc');
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    http.expectNone('/api/v1/wallets');
  });

  it('nome repetido mostra o aviso no campo (US1.2)', () => {
    abrir(null);
    preencher('#nome', 'azul');
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    const p = problema(409, 'wallet.name_taken', 'Já existe uma carteira com esse nome.');
    http.expectOne('/api/v1/wallets').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('#nome-erro')!.textContent).toContain('Já existe uma carteira');
    expect(navegar).not.toHaveBeenCalled();
  });

  it('edição: carrega os dados, mostra saldo e salva com a versão', () => {
    existente({ version: 4 });
    expect(el.querySelector<HTMLInputElement>('#nome')!.value).toBe('Azul');
    expect(el.querySelector<HTMLInputElement>('#saldo')!.value).toBe('500,00');
    expect(el.querySelector('[data-testid="saldo-atual"]')!.textContent).toBe('R$ 500,00');
    expect(el.querySelector('[data-testid="principal"]')).not.toBeNull();

    preencher('#nome', 'Azul 2');
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    const req = http.expectOne('/api/v1/wallets/w-azul');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.version).toBe(4);
    req.flush(carteira());
  });

  it('Tornar principal só aparece fora da principal e recarrega', () => {
    existente({ isPrimary: false });
    clicar('Tornar principal');
    http.expectOne('/api/v1/wallets/w-azul/make-primary').flush(carteira());
    http.expectOne('/api/v1/wallets/w-azul').flush(carteira());
    fixture.detectChanges();
    expect(
      Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.includes('Tornar')),
    ).toBe(false);
  });

  it('arquivar a principal exige escolher outra principal (US1.4)', async () => {
    existente();
    clicar('Arquivar');
    http
      .expectOne('/api/v1/wallets?archived=false')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde', isPrimary: false })]);
    await estabilizar();

    const confirmar = dialogo().querySelector<HTMLButtonElement>('[data-testid="confirmar"]')!;
    expect(confirmar.disabled).toBe(true);
    const select = dialogo().querySelector<HTMLSelectElement>('#nova-principal')!;
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['', 'w-verde']);
    select.value = 'w-verde';
    select.dispatchEvent(new Event('change'));
    await estabilizar();
    confirmar.click();
    await estabilizar();

    const req = http.expectOne('/api/v1/wallets/w-azul/archive');
    expect(req.request.body).toEqual({ newPrimaryWalletId: 'w-verde' });
    req.flush(carteira({ archived: true, isPrimary: false }));
    expect(navegar).toHaveBeenCalledWith('/carteiras');
  });

  it('arquivar a principal única é bloqueado com explicação (US1.4)', () => {
    existente();
    clicar('Arquivar');
    http.expectOne('/api/v1/wallets?archived=false').flush([carteira()]);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-carteira"]')!.textContent).toContain(
      'ela é a única ativa',
    );
    expect(dialogo().querySelector('[data-testid="confirmar"]')).toBeNull();
  });

  it('arquivar uma não principal vai direto', () => {
    existente({ isPrimary: false });
    clicar('Arquivar');
    const req = http.expectOne('/api/v1/wallets/w-azul/archive');
    expect(req.request.body).toEqual({});
    req.flush(carteira({ archived: true }));
  });

  it('carteira arquivada oferece Desarquivar', () => {
    existente({ archived: true, isPrimary: false });
    clicar('Desarquivar');
    http.expectOne('/api/v1/wallets/w-azul/unarchive').flush(carteira());
    http.expectOne('/api/v1/wallets/w-azul').flush(carteira());
  });

  it('com lançamentos não oferece Excluir, só arquivar (US1.3)', () => {
    existente({ hasEntries: true });
    expect(
      Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Excluir'),
    ).toBe(false);
    expect(el.querySelector('[data-testid="so-arquivar"]')!.textContent).toContain(
      'apenas arquivada',
    );
  });

  it('excluir pede confirmação em diálogo', async () => {
    existente({ isPrimary: false });
    clicar('Excluir');
    await estabilizar();
    http.expectNone('/api/v1/wallets/w-azul');
    expect(dialogo().textContent).toContain('Excluir "Azul" definitivamente');
    dialogo().querySelector<HTMLButtonElement>('[data-testid="confirmar"]')!.click();
    await estabilizar();
    const req = http.expectOne('/api/v1/wallets/w-azul');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(navegar).toHaveBeenCalledWith('/carteiras');
  });

  it('excluir a principal com outra ativa exige escolher a nova principal', async () => {
    existente();
    clicar('Excluir');
    http
      .expectOne('/api/v1/wallets?archived=false')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde', isPrimary: false })]);
    await estabilizar();
    const select = dialogo().querySelector<HTMLSelectElement>('#nova-principal')!;
    select.value = 'w-verde';
    select.dispatchEvent(new Event('change'));
    await estabilizar();
    dialogo().querySelector<HTMLButtonElement>('[data-testid="confirmar"]')!.click();
    await estabilizar();
    http
      .expectOne('/api/v1/wallets/w-azul?newPrimaryWalletId=w-verde')
      .flush(null, { status: 204, statusText: 'No Content' });
  });

  it('cancelar o diálogo não chama a API', async () => {
    existente({ isPrimary: false });
    clicar('Excluir');
    await estabilizar();
    Array.from(dialogo().querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === 'Cancelar')!
      .click();
    await estabilizar();
    http.expectNone('/api/v1/wallets/w-azul');
  });

  it('carteira inexistente mostra aviso', () => {
    abrir('x');
    const p = problema(404, 'not_found');
    http.expectOne('/api/v1/wallets/x').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-carteira"]')!.textContent).toContain(
      'não encontrada',
    );
  });
});
