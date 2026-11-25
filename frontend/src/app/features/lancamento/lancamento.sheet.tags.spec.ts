import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { today } from '../../core/money';
import { Tag } from '../../shared/tag-picker/tag.models';
import { bruna, carla, ifood, tag } from '../../shared/tag-picker/testing';
import { carteira } from '../carteiras/testing';
import { compra } from '../cartoes/fatura/testing';
import { cartao } from '../cartoes/testing';
import { Entry } from './entry.models';
import { LancamentoSheet, LancamentoSheetData } from './lancamento.sheet';
import { DADOS_DIALOGO, Dialogo, DialogoRef } from '@/shared/ui/dialogo';
import { Avisos } from '@/shared/ui/avisos';

const mercado = tag({ id: 't-mercado', name: 'Mercado', uses90d: 7 });

function entrada(parcial: Partial<Entry> = {}): Entry {
  return {
    id: 'e1',
    kind: 'expense',
    description: 'Ifood',
    amountCents: 4499,
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

describe('LancamentoSheet — campo Tags (004 US1, FR-003)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function abrir(
    opcoes: {
      tags?: Tag[];
      dados?: LancamentoSheetData;
      tipo?: 'expense' | 'income' | 'card' | 'transfer';
    } = {},
  ) {
    localStorage.clear();
    localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: opcoes.tipo ?? 'expense' }));
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DialogoRef, useValue: { close: vi.fn() } },
        { provide: DADOS_DIALOGO, useValue: opcoes.dados ?? null },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Avisos), 'open').mockReturnValue(undefined as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.match((r) => r.url.startsWith('/api/v1/wallets')).forEach((r) => r.flush([carteira()]));
    http.match((r) => r.url.startsWith('/api/v1/credit-cards')).forEach((r) => r.flush([cartao()]));
    http
      .match('/api/v1/tags?archived=all')
      .forEach((r) => r.flush(opcoes.tags ?? [bruna, carla, ifood, mercado]));
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
  const chip = (nome: string) =>
    el.querySelector<HTMLElement>(`[data-testid="tag-chip"][data-tag="${nome}"]`)!;
  const pausa = (ms: number) => new Promise((r) => setTimeout(r, ms));

  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    http.verify();
    localStorage.clear();
  });

  it('Saída: uma chip e salvar enviam os tagIds (1 toque, SC-001)', async () => {
    await abrir();
    await digitar('#amount-field', '44,99');
    await digitar('#lancamento-descricao', 'Almoço');
    await clicar('[data-tag="Ifood"]');
    await clicar('[data-testid="salvar"]');

    const req = http.expectOne('/api/v1/entries');
    expect(req.request.body).toMatchObject({ description: 'Almoço', tagIds: ['t-ifood'] });
    req.flush(entrada());
  });

  it('sem tags o corpo não leva tagIds', async () => {
    await abrir();
    await digitar('#amount-field', '10');
    await digitar('#lancamento-descricao', 'Pão');
    await clicar('[data-testid="salvar"]');
    const req = http.expectOne('/api/v1/entries');
    expect('tagIds' in req.request.body).toBe(false);
    req.flush(entrada());
  });

  it('Entrada também tem o campo', async () => {
    await abrir({ tipo: 'income' });
    expect(el.querySelector('[data-testid="tag-picker"]')).not.toBeNull();
  });

  it('Transferência não tem campo Tags', async () => {
    await abrir({ tipo: 'transfer' });
    expect(el.querySelector('[data-testid="tag-picker"]')).toBeNull();
  });

  it('Cartão: a compra vai com os tagIds, a de acerto inclusive', async () => {
    await abrir({ tipo: 'card' });
    await digitar('#amount-field', '156,81');
    await digitar('#lancamento-descricao', 'Ifood');
    await clicar('[data-tag="Bruna"]');
    await clicar('[data-tag="Ifood"]');
    await clicar('[data-testid="salvar"]');

    const req = http.expectOne('/api/v1/card-purchases');
    expect(req.request.body.tagIds).toEqual(['t-bruna', 't-ifood']);
    req.flush({ purchases: [compra()], warnings: [] });
  });

  it('a segunda tag de acerto não entra e o aviso sugere Dividir (US1.4)', async () => {
    await abrir();
    await clicar('[data-tag="Bruna"]');
    await clicar('[data-tag="Carla"]');
    expect(el.querySelector('[data-testid="aviso-duas-de-acerto"]')?.textContent).toContain(
      'Só uma tag de acerto por lançamento',
    );
    expect(chip('Carla').getAttribute('aria-pressed')).toBe('false');
  });

  it('sugestão aplica as tags da sugestão se o campo não foi alterado (FR-003)', async () => {
    await abrir();
    await digitar('#lancamento-descricao', 'If');
    await pausa(160);
    http
      .expectOne((r) => r.url.startsWith('/api/v1/entries/suggestions'))
      .flush([{ description: 'Ifood', uses: 4, walletId: null, tagIds: ['t-bruna', 't-ifood'] }]);
    await atualizar();
    await clicar('[data-testid="sugestao"]');
    expect(chip('Bruna').getAttribute('aria-pressed')).toBe('true');
    expect(chip('Ifood').getAttribute('aria-pressed')).toBe('true');
  });

  it('tags escolhidas à mão não são trocadas pela sugestão', async () => {
    await abrir();
    await clicar('[data-tag="Mercado"]');
    await digitar('#lancamento-descricao', 'If');
    await pausa(160);
    http
      .expectOne((r) => r.url.startsWith('/api/v1/entries/suggestions'))
      .flush([{ description: 'Ifood', uses: 4, walletId: null, tagIds: ['t-ifood'] }]);
    await atualizar();
    await clicar('[data-testid="sugestao"]');
    expect(chip('Mercado').getAttribute('aria-pressed')).toBe('true');
    expect(chip('Ifood').getAttribute('aria-pressed')).toBe('false');
  });

  it('edição abre com as tags do lançamento e salva trocando-as (lista vazia remove todas)', async () => {
    await abrir({ dados: { entry: entrada({ tagIds: ['t-bruna', 't-ifood'] }) } });
    expect(chip('Bruna').getAttribute('aria-pressed')).toBe('true');
    expect(chip('Ifood').getAttribute('aria-pressed')).toBe('true');

    await clicar('[data-tag="Ifood"]');
    await clicar('[data-testid="salvar"]');
    const put = http.expectOne('/api/v1/entries/e1');
    expect(put.request.body.tagIds).toEqual(['t-bruna']);
    put.flush(entrada({ version: 2 }));
  });

  it('edição sem nenhuma tag manda lista vazia (para o servidor limpar)', async () => {
    await abrir({ dados: { entry: entrada({ tagIds: ['t-ifood'] }) } });
    await clicar('[data-tag="Ifood"]');
    await clicar('[data-testid="salvar"]');
    const put = http.expectOne('/api/v1/entries/e1');
    expect(put.request.body.tagIds).toEqual([]);
    put.flush(entrada({ version: 2 }));
  });

  it('edição de parcela pergunta o escopo e leva as tags junto', async () => {
    const parcela = compra({
      installmentPlanId: 'plano-1',
      installmentNumber: 2,
      installmentTotal: 4,
      tagIds: ['t-mercado'],
    });
    await abrir({ dados: { purchase: parcela } });
    expect(chip('Mercado').getAttribute('aria-pressed')).toBe('true');
    await clicar('[data-tag="Ifood"]');

    const dialog = TestBed.inject(Dialogo);
    vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of('all') } as never);
    await clicar('[data-testid="salvar"]');

    const put = http.expectOne('/api/v1/card-purchases/p1');
    expect(put.request.body).toMatchObject({ scope: 'all', tagIds: ['t-mercado', 't-ifood'] });
    put.flush(parcela);
  });

  it('"Salvar e lançar outro" reabre sem as tags', async () => {
    await abrir();
    await digitar('#amount-field', '10');
    await digitar('#lancamento-descricao', 'Pão');
    await clicar('[data-tag="Ifood"]');
    await clicar('[data-testid="salvar-outro"]');
    http.expectOne('/api/v1/entries').flush(entrada());
    await atualizar();
    expect(chip('Ifood').getAttribute('aria-pressed')).toBe('false');
  });
});
