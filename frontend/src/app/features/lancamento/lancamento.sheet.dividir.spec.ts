import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { today } from '../../core/money';
import { Tag } from '../../shared/tag-picker/tag.models';
import { bruna, carla, ifood } from '../../shared/tag-picker/testing';
import { carteira } from '../carteiras/testing';
import { compra } from '../cartoes/fatura/testing';
import { cartao } from '../cartoes/testing';
import { Entry } from './entry.models';
import { LancamentoSheet, LancamentoSheetData } from './lancamento.sheet';

const TAGS: Tag[] = [bruna, carla, ifood];

function entrada(parcial: Partial<Entry> = {}): Entry {
  return {
    id: 'e1',
    kind: 'expense',
    description: 'Jacomar Niver Carla',
    amountCents: 11990,
    date: today(),
    referenceMonth: today().slice(0, 7),
    status: 'settled',
    walletId: 'w-azul',
    createdAt: '2026-03-10T15:00:00Z',
    version: 1,
    tagIds: [],
    settlementTagId: null,
    ...parcial,
  };
}

describe('LancamentoSheet — Dividir (004 US4, FR-012)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function abrir(
    opcoes: { dados?: LancamentoSheetData; tipo?: 'expense' | 'income' | 'card' | 'transfer' } = {},
  ) {
    localStorage.clear();
    localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: opcoes.tipo ?? 'expense' }));
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: opcoes.dados ?? null },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.match((r) => r.url.startsWith('/api/v1/wallets')).forEach((r) => r.flush([carteira()]));
    http.match((r) => r.url.startsWith('/api/v1/credit-cards')).forEach((r) => r.flush([cartao()]));
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush(TAGS));
    await atualizar();
  }
  async function digitar(seletor: string, valor: string) {
    const campo = el.querySelector<HTMLInputElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  }
  async function clicar(seletor: string) {
    el.querySelector<HTMLElement>(seletor)!.click();
    await atualizar();
  }
  async function abrirDividir() {
    await clicar('[data-testid="dividir"]');
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush(TAGS));
    await atualizar();
  }
  const painel = () => el.querySelector('[data-testid="dividir-painel"]');
  const calculados = () =>
    Array.from(el.querySelectorAll('[data-testid="parte-valor-calculado"]')).map((n) =>
      n.textContent!.replace(/\s/g, ' ').trim(),
    );
  const salvar = () => el.querySelector<HTMLButtonElement>('[data-testid="salvar"]')!;

  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    http.verify();
    localStorage.clear();
  });

  it('Saída: Dividir em partes iguais envia /entries/split com as partes na ordem (US4.1)', async () => {
    await abrir();
    await digitar('#amount-field', '239,80');
    await digitar('#lancamento-descricao', 'Jacomar Niver Carla');
    await abrirDividir();

    expect(painel()).not.toBeNull();
    expect(calculados()).toEqual(['R$ 119,90', 'R$ 119,90']);
    expect(el.querySelector<HTMLSelectElement>('#parte-pessoa-1')!.value).toBe('t-bruna');

    await clicar('[data-testid="salvar"]');
    const req = http.expectOne('/api/v1/entries/split');
    expect(req.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(req.request.body).toMatchObject({
      kind: 'expense',
      description: 'Jacomar Niver Carla',
      amount: '239,80',
      split: {
        mode: 'equal',
        parts: [{ settlementTagId: null }, { settlementTagId: 't-bruna' }],
      },
    });
    req.flush([
      entrada({ splitCount: 2, splitGroupId: 'g1' }),
      entrada({ id: 'e2', settlementTagId: 't-bruna', splitCount: 2, splitGroupId: 'g1' }),
    ]);
  });

  it('R$ 100,00 em 3 mostra 33,34 / 33,33 / 33,33 (US4.4)', async () => {
    await abrir();
    await digitar('#amount-field', '100');
    await abrirDividir();
    await clicar('[data-testid="parte-adicionar"]');
    expect(calculados()).toEqual(['R$ 33,34', 'R$ 33,33', 'R$ 33,33']);
  });

  it('valores informados: mostra "Faltam"/"Sobram" e bloqueia o salvar (US4.3)', async () => {
    await abrir();
    await digitar('#amount-field', '100');
    await digitar('#lancamento-descricao', 'Presente');
    await abrirDividir();
    await clicar('[data-testid="modo-amounts"]');

    const valores = () => el.querySelectorAll<HTMLInputElement>('[data-testid="parte-valor"]');
    await digitar('[aria-label="Valor da parte 1"]', '40');
    await digitar('[aria-label="Valor da parte 2"]', '50');
    expect(el.querySelector('[data-testid="erro-divisao"]')?.textContent).toContain(
      'Faltam R$ 10,00',
    );
    expect(salvar().disabled).toBe(true);

    await digitar('[aria-label="Valor da parte 2"]', '70');
    expect(el.querySelector('[data-testid="erro-divisao"]')?.textContent).toContain(
      'Sobram R$ 10,00',
    );
    expect(salvar().disabled).toBe(true);

    await digitar('[aria-label="Valor da parte 2"]', '60');
    expect(valores().length).toBe(2);
    expect(el.querySelector('[data-testid="erro-divisao"]')).toBeNull();
    expect(salvar().disabled).toBe(false);

    await clicar('[data-testid="salvar"]');
    const req = http.expectOne('/api/v1/entries/split');
    expect(req.request.body.split).toEqual({
      mode: 'amounts',
      parts: [
        { settlementTagId: null, amount: '40' },
        { settlementTagId: 't-bruna', amount: '60' },
      ],
    });
    req.flush([entrada(), entrada({ id: 'e2' })]);
  });

  it('não repete a pessoa: a escolhida em uma parte some das outras', async () => {
    await abrir();
    await digitar('#amount-field', '100');
    await abrirDividir();
    await clicar('[data-testid="parte-adicionar"]');
    const opcoes = (i: number) =>
      Array.from(el.querySelectorAll<HTMLOptionElement>(`#parte-pessoa-${i} option`)).map(
        (o) => o.value,
      );
    expect(opcoes(0)).toEqual(['']);
    expect(opcoes(1)).toEqual(['t-bruna']);
    expect(el.querySelector<HTMLSelectElement>('#parte-pessoa-2')!.value).toBe('t-carla');
    // Sem mais ninguém livre, a quarta parte repetiria "Eu" e o salvar fica bloqueado.
    await clicar('[data-testid="parte-adicionar"]');
    expect(el.querySelector('[data-testid="erro-divisao"]')).not.toBeNull();
    expect(salvar().disabled).toBe(true);
  });

  it('limita a 10 partes e exige ao menos 2', async () => {
    await abrir();
    await digitar('#amount-field', '100');
    await abrirDividir();
    expect(el.querySelector('[aria-label^="Remover parte"]')).toBeNull();
    for (let i = 0; i < 8; i++) await clicar('[data-testid="parte-adicionar"]');
    expect(el.querySelectorAll('[data-testid="parte"]').length).toBe(10);
    expect(el.querySelector('[data-testid="parte-adicionar"]')).toBeNull();
  });

  it('tag de acerto já escolhida vira a pessoa da segunda parte e não vai em tagIds', async () => {
    await abrir();
    await digitar('#amount-field', '50');
    await digitar('#lancamento-descricao', 'Almoço');
    await clicar('[data-tag="Carla"]');
    await clicar('[data-tag="Ifood"]');
    await abrirDividir();
    expect(el.querySelector<HTMLSelectElement>('#parte-pessoa-1')!.value).toBe('t-carla');

    await clicar('[data-testid="salvar"]');
    const req = http.expectOne('/api/v1/entries/split');
    expect(req.request.body.tagIds).toEqual(['t-ifood']);
    expect(req.request.body.split.parts[1]).toEqual({ settlementTagId: 't-carla' });
    req.flush([entrada(), entrada({ id: 'e2' })]);
  });

  it('o aviso da segunda tag de acerto abre o Dividir', async () => {
    await abrir();
    await digitar('#amount-field', '50');
    await clicar('[data-tag="Bruna"]');
    await clicar('[data-tag="Carla"]');
    await clicar('[data-testid="tag-dividir"]');
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush(TAGS));
    await atualizar();
    expect(painel()).not.toBeNull();
  });

  it('"Não dividir" volta ao salvamento normal', async () => {
    await abrir();
    await digitar('#amount-field', '10');
    await digitar('#lancamento-descricao', 'Pão');
    await abrirDividir();
    await clicar('[data-testid="dividir-cancelar"]');
    expect(painel()).toBeNull();
    await clicar('[data-testid="salvar"]');
    http.expectOne('/api/v1/entries').flush(entrada());
  });

  it('Entrada e Transferência não têm Dividir', async () => {
    await abrir({ tipo: 'income' });
    expect(el.querySelector('[data-testid="dividir"]')).toBeNull();
    await clicar('[role="radio"]:nth-child(3)');
    expect(el.querySelector('[data-testid="dividir"]')).toBeNull();
  });

  it('Cartão: compra dividida vai para /card-purchases/split (US4.1)', async () => {
    await abrir({ tipo: 'card' });
    await digitar('#amount-field', '239,80');
    await digitar('#lancamento-descricao', 'Jacomar Niver Carla');
    await abrirDividir();
    expect(calculados()).toEqual(['R$ 119,90', 'R$ 119,90']);

    await clicar('[data-testid="salvar"]');
    const req = http.expectOne('/api/v1/card-purchases/split');
    expect(req.request.body).toMatchObject({
      creditCardId: 'c-azul',
      amount: '239,80',
      split: { mode: 'equal', parts: [{ settlementTagId: null }, { settlementTagId: 't-bruna' }] },
    });
    req.flush({
      purchases: [compra({ splitCount: 2 }), compra({ id: 'p2', splitCount: 2 })],
      warnings: [],
    });
  });

  it('Cartão parcelado: divide o total e leva as parcelas (US4.2)', async () => {
    await abrir({ tipo: 'card' });
    await digitar('#amount-field', '207,90');
    await digitar('#lancamento-descricao', 'MYNO');
    const parcelas = el.querySelector<HTMLSelectElement>('#lancamento-parcelas')!;
    parcelas.value = '4';
    parcelas.dispatchEvent(new Event('change'));
    await atualizar();
    await abrirDividir();
    await clicar('[data-testid="parte-adicionar"]');
    await clicar('[data-testid="modo-amounts"]');
    await digitar('[aria-label="Valor da parte 1"]', '110,94');
    await digitar('[aria-label="Valor da parte 2"]', '84,97');
    await digitar('[aria-label="Valor da parte 3"]', '11,99');

    await clicar('[data-testid="salvar"]');
    const req = http.expectOne('/api/v1/card-purchases/split');
    expect(req.request.body.installments).toEqual({ total: 4, mode: 'total_amount' });
    expect(req.request.body.split.parts).toHaveLength(3);
    req.flush({ purchases: [compra()], warnings: [] });
  });

  it('com "valor da parcela" o total dividido é valor × N', async () => {
    await abrir({ tipo: 'card' });
    await digitar('#amount-field', '25');
    const parcelas = el.querySelector<HTMLSelectElement>('#lancamento-parcelas')!;
    parcelas.value = '4';
    parcelas.dispatchEvent(new Event('change'));
    await atualizar();
    Array.from(el.querySelectorAll<HTMLElement>('[role="radio"]'))
      .find((b) => b.textContent!.includes('Valor da parcela'))!
      .click();
    await atualizar();
    await abrirDividir();
    expect(calculados()).toEqual(['R$ 50,00', 'R$ 50,00']);
  });

  it('Cartão: crédito/estorno e parcelamento em andamento não podem ser divididos', async () => {
    await abrir({ tipo: 'card' });
    await digitar('#amount-field', '-20');
    expect(el.querySelector('[data-testid="dividir"]')).toBeNull();
    await digitar('#amount-field', '20');
    expect(el.querySelector('[data-testid="dividir"]')).not.toBeNull();
    await clicar('[data-testid="em-andamento"]');
    expect(el.querySelector('[data-testid="dividir"]')).toBeNull();
  });

  it('edição não oferece Dividir', async () => {
    await abrir({ dados: { entry: entrada() } });
    expect(el.querySelector('[data-testid="dividir"]')).toBeNull();
  });
});

describe('LancamentoSheet — selo "Dividida em N" (004 FR-014)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function abrir(dados: LancamentoSheetData) {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: dados },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.match((r) => r.url.startsWith('/api/v1/wallets')).forEach((r) => r.flush([carteira()]));
    http.match((r) => r.url.startsWith('/api/v1/credit-cards')).forEach((r) => r.flush([cartao()]));
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush(TAGS));
    await atualizar();
  }

  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    http.verify();
    localStorage.clear();
  });

  it('lançamento não dividido não tem selo', async () => {
    await abrir({ entry: entrada() });
    expect(el.querySelector('[data-testid="selo-divisao"]')).toBeNull();
  });

  it('008 FR-008: o formulário de edição mostra "via Claude" só se o lançamento foi criado pelo Claude', async () => {
    await abrir({ entry: entrada({ createdVia: 'claude' }) });
    expect(el.querySelector('[data-testid="selo-via-claude"]')?.textContent).toContain(
      'via Claude',
    );
  });

  it('008 FR-008: lançamento do app não tem o selo "via Claude" no formulário', async () => {
    await abrir({ entry: entrada({ createdVia: 'app' }) });
    expect(el.querySelector('[data-testid="selo-via-claude"]')).toBeNull();
  });

  it('Saída dividida: selo e, ao tocar, as outras partes', async () => {
    const parte = entrada({ splitCount: 2, splitGroupId: 'g1' });
    await abrir({ entry: parte });
    const selo = el.querySelector<HTMLElement>('[data-testid="selo-divisao"]')!;
    expect(selo.textContent).toContain('Dividida em 2');

    selo.click();
    await atualizar();
    const busca = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
    expect(new URL(busca.request.urlWithParams, 'http://x').searchParams.get('q')).toBe(
      'Jacomar Niver Carla',
    );
    expect(new URL(busca.request.urlWithParams, 'http://x').searchParams.get('from')).toBe(
      parte.date,
    );
    busca.flush({
      items: [
        { type: 'expense', entry: parte },
        {
          type: 'expense',
          entry: entrada({
            id: 'e2',
            amountCents: 11990,
            settlementTagId: 't-bruna',
            splitCount: 2,
            splitGroupId: 'g1',
          }),
        },
        { type: 'expense', entry: entrada({ id: 'e9', splitGroupId: 'outro', splitCount: 2 }) },
      ],
      totalCents: 0,
      totalCount: 3,
    });
    http.expectOne('/api/v1/tags?archived=all').flush(TAGS);
    await atualizar();

    const linhas = Array.from(el.querySelectorAll('[data-testid="outra-parte"]')).map((n) =>
      n.textContent!.replace(/\s/g, ' ').trim(),
    );
    expect(linhas).toEqual(['Bruna — R$ 119,90']);
  });

  it('compra dividida: selo na edição da fatura', async () => {
    await abrir({ purchase: compra({ splitCount: 3, splitGroupId: 'g2' }) });
    expect(el.querySelector('[data-testid="selo-divisao"]')?.textContent).toContain(
      'Dividida em 3',
    );
  });
});
