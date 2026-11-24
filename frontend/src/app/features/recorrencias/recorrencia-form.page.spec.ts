import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { problema } from '../../core/auth/testing';
import { today } from '../../core/money';
import { cartao } from '../cartoes/testing';
import { carteira } from '../carteiras/testing';
import { RecorrenciaFormPage } from './recorrencia-form.page';
import { recorrencia } from './testing';

describe('RecorrenciaFormPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<RecorrenciaFormPage>;
  let el: HTMLElement;
  let navegar: ReturnType<typeof vi.spyOn>;

  function abrir(id: string | null) {
    TestBed.configureTestingModule({
      imports: [RecorrenciaFormPage],
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
    fixture = TestBed.createComponent(RecorrenciaFormPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http
      .expectOne('/api/v1/wallets?archived=all')
      .flush([
        carteira(),
        carteira({ id: 'w-verde', name: 'Verde', isPrimary: false }),
        carteira({ id: 'w-velha', name: 'Velha', isPrimary: false, archived: true }),
      ]);
    http
      .expectOne('/api/v1/credit-cards?archived=all')
      .flush([
        cartao({ id: 'c-azul', name: 'Azul' }),
        cartao({ id: 'c-velho', name: 'Velho', archived: true }),
      ]);
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush([]));
    fixture.detectChanges();
    fixture.detectChanges();
  }
  const aberta = (parcial = {}) => {
    abrir('r-tim');
    http.expectOne('/api/v1/recurrences/r-tim').flush(recorrencia(parcial));
    fixture.detectChanges();
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush([]));
    fixture.detectChanges();
    fixture.detectChanges();
  };
  const preencher = (seletor: string, valor: string) => {
    const campo = el.querySelector<HTMLInputElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const escolher = (seletor: string, valor: string) => {
    const campo = el.querySelector<HTMLSelectElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const clicarId = (testid: string) => {
    el.querySelector<HTMLButtonElement>(`[data-testid="${testid}"]`)!.click();
    fixture.detectChanges();
  };
  const clicar = (texto: string) => {
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === texto)!
      .click();
    fixture.detectChanges();
  };
  const salvar = () => {
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };
  const estabilizar = async () => {
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r, 250));
    TestBed.inject(ApplicationRef).tick();
  };
  const dialogo = () => TestBed.inject(OverlayContainer).getContainerElement();
  const mesNoDialogo = async (mes: string | null, botao = 'confirmar') => {
    await estabilizar();
    if (mes !== null) {
      const campo = dialogo().querySelector<HTMLInputElement>('[data-testid="a-partir-de-mes"]')!;
      campo.value = mes;
      campo.dispatchEvent(new Event('input'));
      await estabilizar();
    }
    dialogo().querySelector<HTMLButtonElement>(`[data-testid="${botao}"]`)!.click();
    await estabilizar();
  };

  afterEach(() => http.verify());

  it('nova: padrões (Saída, carteira principal, mês de hoje) e criação com a chave de idempotência (US1.1)', () => {
    abrir(null);
    expect(el.querySelector('[aria-checked="true"][data-testid="tipo-expense"]')).not.toBeNull();
    expect(el.querySelector<HTMLSelectElement>('#carteira')!.value).toBe('w-azul');
    expect(el.querySelector<HTMLInputElement>('#mes-inicial')!.value).toBe(today().slice(0, 7));
    // a carteira arquivada não é oferecida
    expect(
      Array.from(el.querySelectorAll('#carteira option')).map((o) => o.textContent?.trim()),
    ).toEqual(['Escolha uma carteira', 'Azul', 'Verde']);

    preencher('#descricao', ' Tim ');
    preencher('#amount-field', '40+4,99');
    preencher('#dia', '10');
    preencher('#mes-inicial', '2026-03');
    salvar();

    const req = http.expectOne('/api/v1/recurrences');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(req.request.body).toEqual({
      kind: 'expense',
      description: 'Tim',
      amount: '40+4,99',
      dayOfMonth: 10,
      walletId: 'w-azul',
      creditCardId: null,
      tagIds: [],
      startMonth: '2026-03',
      endType: 'none',
      endMonth: null,
      occurrenceLimit: null,
      referenceMonthRule: 'same_month',
    });
    req.flush(recorrencia(), { status: 201, statusText: 'Created' });
    expect(navegar).toHaveBeenCalledWith('/recorrencias');
  });

  it('Entrada com "mês seguinte" (US2.1)', () => {
    abrir(null);
    clicarId('tipo-income');
    preencher('#descricao', 'Salário');
    preencher('#amount-field', '1200');
    preencher('#dia', '25');
    escolher('#regra', 'next_month');
    salvar();
    const req = http.expectOne('/api/v1/recurrences');
    expect(req.request.body).toMatchObject({
      kind: 'income',
      walletId: 'w-azul',
      dayOfMonth: 25,
      referenceMonthRule: 'next_month',
    });
    req.flush(recorrencia({ kind: 'income' }), { status: 201, statusText: 'Created' });
  });

  it('Compra no cartão pede o cartão e não envia a regra de mês (US1.3)', () => {
    abrir(null);
    clicarId('tipo-card_purchase');
    expect(el.querySelector('#regra')).toBeNull();
    expect(el.querySelector('#carteira')).toBeNull();
    expect(el.querySelector<HTMLSelectElement>('#cartao')!.value).toBe('c-azul');
    preencher('#descricao', 'Prime Video');
    preencher('#amount-field', '19,90');
    preencher('#dia', '2');
    salvar();
    const req = http.expectOne('/api/v1/recurrences');
    expect(req.request.body).toMatchObject({
      kind: 'card_purchase',
      creditCardId: 'c-azul',
      walletId: null,
      referenceMonthRule: null,
    });
    req.flush(recorrencia({ kind: 'card_purchase' }), { status: 201, statusText: 'Created' });
  });

  it('términos: até um mês e após N ocorrências (US1.5)', () => {
    abrir(null);
    preencher('#descricao', 'Curso');
    preencher('#amount-field', '10');
    clicarId('termino-after_count');
    preencher('#ocorrencias', '6');
    salvar();
    const req = http.expectOne('/api/v1/recurrences');
    expect(req.request.body).toMatchObject({
      endType: 'after_count',
      occurrenceLimit: 6,
      endMonth: null,
    });
    req.flush(recorrencia(), { status: 201, statusText: 'Created' });

    clicarId('termino-until_month');
    preencher('#mes-final', '2026-01');
    preencher('#mes-inicial', '2026-03');
    salvar();
    expect(el.querySelector('[data-testid="erro-termino"]')!.textContent).toContain(
      'não pode ser antes',
    );
    preencher('#mes-final', '2026-12');
    salvar();
    const req2 = http.expectOne('/api/v1/recurrences');
    expect(req2.request.body).toMatchObject({
      endType: 'until_month',
      endMonth: '2026-12',
      occurrenceLimit: null,
    });
    req2.flush(recorrencia(), { status: 201, statusText: 'Created' });
  });

  it('campos inválidos não chamam a API e mostram as mensagens', () => {
    abrir(null);
    preencher('#amount-field', 'abc');
    preencher('#dia', '32');
    salvar();
    fixture.detectChanges();
    http.expectNone('/api/v1/recurrences');
    expect(el.querySelector('[data-testid="erro-descricao"]')!.textContent).toContain(
      'Informe a descrição',
    );
    expect(el.querySelector('[data-testid="erro-valor"]')!.textContent).toContain('Valor inválido');
    expect(el.querySelector('[data-testid="erro-dia"]')!.textContent).toContain('1 a 31');
  });

  it('erro de validação do servidor aparece no aviso', () => {
    abrir(null);
    preencher('#descricao', 'Tim');
    preencher('#amount-field', '44,99');
    salvar();
    const p = problema(422, 'wallet.archived', 'A carteira está arquivada.');
    http.expectOne('/api/v1/recurrences').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-recorrencia"]')!.textContent).toContain(
      'A carteira está arquivada.',
    );
    expect(navegar).not.toHaveBeenCalled();
  });

  it('edição: carrega os dados; tipo e mês inicial não mudam', () => {
    aberta({ amountCents: 4499, version: 3 });
    expect(el.querySelector<HTMLInputElement>('#descricao')!.value).toBe('Tim');
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('44,99');
    expect(el.querySelector<HTMLInputElement>('#dia')!.value).toBe('10');
    expect(el.querySelector<HTMLSelectElement>('#carteira')!.value).toBe('w-azul');
    expect(el.querySelector<HTMLInputElement>('#mes-inicial')!.readOnly).toBe(true);
    expect(el.querySelector<HTMLButtonElement>('[data-testid="tipo-income"]')!.disabled).toBe(true);
    expect(el.querySelector('[data-testid="situacao"]')!.textContent).toBe('Ativa');
  });

  it('edição: salvar pergunta "a partir de qual mês" (padrão o mês de hoje) e envia fromMonth e version (US3.1)', async () => {
    aberta({ version: 3 });
    preencher('#amount-field', '50,99');
    salvar();
    await estabilizar();
    const campo = dialogo().querySelector<HTMLInputElement>('[data-testid="a-partir-de-mes"]')!;
    expect(campo.value).toBe(today().slice(0, 7));
    await mesNoDialogo('2026-09');

    const req = http.expectOne('/api/v1/recurrences/r-tim');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toMatchObject({
      kind: 'expense',
      amount: '50,99',
      fromMonth: '2026-09',
      version: 3,
    });
    req.flush({ ...recorrencia({ amountCents: 5099, version: 4 }), affectedCount: 16 });
    http
      .expectOne('/api/v1/recurrences/r-tim')
      .flush(recorrencia({ amountCents: 5099, version: 4 }));
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush([]));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="info-recorrencia"]')!.textContent).toContain(
      '16 lançamento(s) atualizado(s)',
    );
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('50,99');
  });

  it('edição: cancelar o diálogo não chama a API', async () => {
    aberta();
    salvar();
    await mesNoDialogo(null, 'cancelar');
    http.expectNone('/api/v1/recurrences/r-tim');
  });

  it('conflito de versão (409) aparece no aviso (US3)', async () => {
    aberta();
    salvar();
    await mesNoDialogo('2026-09');
    const p = problema(409, 'concurrency_conflict', 'Alguém alterou esta recorrência.');
    http.expectOne('/api/v1/recurrences/r-tim').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-recorrencia"]')!.textContent).toContain(
      'Alguém alterou',
    );
  });

  it('Encerrar pede o mês e confirmação, mostra quantos lançamentos saíram e fica encerrada (US3.2)', async () => {
    aberta();
    clicar('Encerrar');
    await estabilizar();
    expect(dialogo().textContent).toContain('Encerrar "Tim"?');
    await mesNoDialogo('2026-10');

    const req = http.expectOne('/api/v1/recurrences/r-tim/end');
    expect(req.request.body).toEqual({ fromMonth: '2026-10' });
    const encerrada = recorrencia({ status: 'ended', endType: 'until_month', endMonth: '2026-09' });
    req.flush({ recurrence: encerrada, deletedCount: 15, deletionBatchId: 'b1' });
    http.expectOne('/api/v1/recurrences/r-tim').flush(encerrada);
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush([]));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="info-recorrencia"]')!.textContent).toContain(
      '15 lançamento(s) excluído(s)',
    );
    expect(el.querySelector('[data-testid="situacao"]')!.textContent).toBe('Encerrada');
    expect(
      Array.from(el.querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Encerrar'),
    ).toBe(false);
  });

  it('Excluir pede confirmação e volta para a lista (FR-007)', async () => {
    aberta();
    clicar('Excluir');
    await estabilizar();
    dialogo().querySelector<HTMLButtonElement>('[data-testid="confirmar"]')!.click();
    await estabilizar();
    const req = http.expectOne('/api/v1/recurrences/r-tim');
    expect(req.request.method).toBe('DELETE');
    req.flush({ deletedCount: 15, unlinkedCount: 1 });
    expect(navegar).toHaveBeenCalledWith('/recorrencias');
  });

  it('Excluir cancelado não chama a API', async () => {
    aberta();
    clicar('Excluir');
    await estabilizar();
    Array.from(dialogo().querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === 'Cancelar')!
      .click();
    await estabilizar();
    http.expectNone('/api/v1/recurrences/r-tim');
  });

  it('carteira arquivada da recorrência segue escolhida, com o aviso "Revise"', () => {
    aberta({ walletId: 'w-velha', needsReview: true, reviewReason: 'wallet_archived' });
    expect(el.querySelector<HTMLSelectElement>('#carteira')!.value).toBe('w-velha');
    expect(el.querySelector('#carteira option[value="w-velha"]')!.textContent).toContain(
      'arquivada',
    );
    expect(el.querySelector('[data-testid="revisar"]')!.textContent).toBe(
      'Revise: carteira arquivada',
    );
  });

  it('recorrência inexistente mostra "não encontrada"', () => {
    abrir('r-x');
    const p = problema(404, 'not_found', 'Não encontrada.');
    http.expectOne('/api/v1/recurrences/r-x').flush(p.body, p.opts);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-recorrencia"]')!.textContent).toContain(
      'Recorrência não encontrada.',
    );
  });
});
