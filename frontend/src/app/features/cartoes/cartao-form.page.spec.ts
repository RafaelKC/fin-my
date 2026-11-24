import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { problema } from '../../core/auth/testing';
import { carteira } from '../carteiras/testing';
import { CartaoFormPage } from './cartao-form.page';
import { cartao } from './testing';

describe('CartaoFormPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<CartaoFormPage>;
  let el: HTMLElement;
  let navegar: ReturnType<typeof vi.spyOn>;

  const verde = () => carteira({ id: 'w-verde', name: 'Verde', isPrimary: false });

  function abrir(id: string | null) {
    TestBed.configureTestingModule({
      imports: [CartaoFormPage],
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
    fixture = TestBed.createComponent(CartaoFormPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }
  const novo = (carteiras = [carteira(), verde()]) => {
    abrir(null);
    http.expectOne('/api/v1/wallets?archived=all').flush(carteiras);
    fixture.detectChanges();
  };
  const existente = (parcial = {}, carteiras = [carteira(), verde()]) => {
    abrir('c-azul');
    http.expectOne('/api/v1/wallets?archived=all').flush(carteiras);
    http.expectOne('/api/v1/credit-cards/c-azul').flush(cartao(parcial));
    fixture.detectChanges();
  };
  const preencher = (id: string, valor: string) => {
    const campo = el.querySelector<HTMLInputElement>(id)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const salvar = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
  const clicar = (texto: string) => {
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === texto)!
      .click();
    fixture.detectChanges();
  };
  const dialogo = () => TestBed.inject(OverlayContainer).getContainerElement();
  const estabilizar = async () => {
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r, 250));
    TestBed.inject(ApplicationRef).tick();
  };

  afterEach(() => http.verify());

  it('novo: 10 cores, dias padrão e carteira principal pré-escolhida', () => {
    novo();
    expect(el.querySelectorAll('.amostra').length).toBe(10);
    expect(el.querySelector<HTMLInputElement>('#fechamento')!.value).toBe('18');
    expect(el.querySelector<HTMLInputElement>('#vencimento')!.value).toBe('25');
    expect(el.querySelector<HTMLSelectElement>('#carteira')!.value).toBe('w-azul');
    expect(el.querySelector('[data-testid="limite-previa"]')!.textContent).toBe('Sem limite');
  });

  it('novo: cria "Azul" (fecha 18, vence 25) e volta para a lista (US1.1)', () => {
    novo();
    preencher('#nome', ' Azul ');
    preencher('#limite', '3000');
    expect(el.querySelector('[data-testid="limite-previa"]')!.textContent).toBe('= R$ 3.000,00');
    salvar();

    const req = http.expectOne('/api/v1/credit-cards');
    expect(req.request.body).toEqual({
      name: 'Azul',
      closingDay: 18,
      dueDay: 25,
      paymentWalletId: 'w-azul',
      color: '#1E88E5',
      limit: '3000',
    });
    req.flush(cartao(), { status: 201, statusText: 'Created' });
    expect(navegar).toHaveBeenCalledWith('/cartoes');
  });

  it('embutido (primeiros passos): sem título nem "Voltar"; ao salvar avisa em vez de navegar', () => {
    abrir(null);
    fixture.componentRef.setInput('embutido', true);
    let avisos = 0;
    fixture.componentInstance.salvou.subscribe(() => avisos++);
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    fixture.detectChanges();
    expect(el.querySelector('h1')).toBeNull();
    expect(el.querySelector('.voltar')).toBeNull();
    preencher('#nome', 'Azul');
    salvar();
    http.expectOne('/api/v1/credit-cards').flush(cartao(), { status: 201, statusText: 'Created' });
    expect(avisos).toBe(1);
    expect(navegar).not.toHaveBeenCalled();
  });

  it('limite vazio é enviado como null (sem limite)', () => {
    novo();
    preencher('#nome', 'Verde');
    salvar();
    expect(http.expectOne('/api/v1/credit-cards').request.body.limit).toBeNull();
  });

  it('nome vazio, dia fora de 1–31 ou limite inválido não chamam a API', () => {
    novo();
    salvar();
    fixture.detectChanges();
    expect(el.textContent).toContain('Informe o nome do cartão.');
    preencher('#nome', 'Azul');
    preencher('#fechamento', '32');
    salvar();
    fixture.detectChanges();
    expect(el.querySelector('#fechamento-erro')!.textContent).toContain('1 a 31');
    preencher('#fechamento', '18');
    preencher('#limite', 'abc');
    salvar();
    preencher('#limite', '-5');
    expect(el.querySelector('[data-testid="limite-previa"]')!.textContent).toContain('negativo');
    salvar();
    http.expectNone('/api/v1/credit-cards');
  });

  it('nome repetido mostra o aviso no campo (US1.2)', () => {
    novo();
    preencher('#nome', 'AZUL');
    salvar();
    const p = problema(409, 'credit_card.name_taken', 'Já existe um cartão com esse nome.');
    http.expectOne('/api/v1/credit-cards').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('#nome-erro')!.textContent).toContain('Já existe um cartão');
    expect(navegar).not.toHaveBeenCalled();
  });

  it('sem carteiras ativas, orienta a cadastrar uma antes', () => {
    novo([carteira({ archived: true })]);
    expect(el.querySelector('[data-testid="sem-carteiras"]')).not.toBeNull();
    expect(el.querySelector('form')).toBeNull();
  });

  it('edição: carrega os dados e salva com a versão', () => {
    existente({ version: 4, limitCents: 300000 });
    expect(el.querySelector<HTMLInputElement>('#nome')!.value).toBe('Azul');
    expect(el.querySelector<HTMLInputElement>('#limite')!.value).toBe('3000,00');

    preencher('#vencimento', '28');
    salvar();
    const req = http.expectOne('/api/v1/credit-cards/c-azul');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.version).toBe(4);
    expect(req.request.body.dueDay).toBe(28);
    req.flush(cartao());
    expect(navegar).toHaveBeenCalledWith('/cartoes');
  });

  it('edição: mantém a carteira de pagamento arquivada na lista, sem oferecer outras arquivadas', () => {
    existente({ paymentWalletId: 'w-velha' }, [
      carteira({ isPrimary: true }),
      carteira({ id: 'w-velha', name: 'Velha', archived: true }),
      carteira({ id: 'w-morta', name: 'Morta', archived: true }),
    ]);
    const opcoes = Array.from(el.querySelectorAll<HTMLOptionElement>('#carteira option')).map(
      (o) => o.value,
    );
    expect(opcoes).toEqual(['w-azul', 'w-velha']);
  });

  it('carteira arquivada recusada pela API aparece no campo', () => {
    existente();
    salvar();
    const p = problema(422, 'wallet.archived', 'Esta carteira está arquivada.');
    http.expectOne('/api/v1/credit-cards/c-azul').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('#carteira-erro')!.textContent).toContain('arquivada');
  });

  it('cartão com compras só pode ser arquivado, não excluído (US1.3, RT-11)', () => {
    existente({ hasPurchases: true });
    expect(el.querySelector('[data-testid="so-arquivar"]')).not.toBeNull();
    expect(
      Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Excluir'),
    ).toBe(false);

    clicar('Arquivar');
    http.expectOne('/api/v1/credit-cards/c-azul/archive').flush(cartao({ archived: true }));
    expect(navegar).toHaveBeenCalledWith('/cartoes');
  });

  it('cartão arquivado oferece Desarquivar e recarrega', () => {
    existente({ archived: true });
    expect(el.querySelector('[data-testid="arquivado"]')).not.toBeNull();
    clicar('Desarquivar');
    http.expectOne('/api/v1/credit-cards/c-azul/unarchive').flush(cartao());
    http.expectOne('/api/v1/credit-cards/c-azul').flush(cartao());
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="arquivado"]')).toBeNull();
  });

  it('excluir cartão sem compras pede confirmação e exclui', async () => {
    existente();
    clicar('Excluir');
    await estabilizar();
    dialogo().querySelector<HTMLButtonElement>('[data-testid="confirmar"]')!.click();
    await estabilizar();
    const req = http.expectOne('/api/v1/credit-cards/c-azul');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(navegar).toHaveBeenCalledWith('/cartoes');
  });

  it('cartão inexistente mostra "não encontrado"', () => {
    abrir('c-x');
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    const p = problema(404, 'not_found', 'Cartão não encontrado.');
    http.expectOne('/api/v1/credit-cards/c-x').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-cartao"]')!.textContent).toContain(
      'não encontrado',
    );
  });
});
