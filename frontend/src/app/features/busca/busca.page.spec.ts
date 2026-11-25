import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { bruna } from '../../shared/tag-picker/testing';
import { carteira } from '../carteiras/testing';
import { Entry, LedgerItem, SearchResult } from '../lancamento/entry.models';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { BuscaPage } from './busca.page';
import { Avisos } from '@/shared/ui/avisos';

const entry = (id: string, p: Partial<Entry> = {}): Entry => ({
  id,
  kind: 'expense',
  description: 'Uber',
  amountCents: 2500,
  date: '2026-03-10',
  referenceMonth: '2026-03',
  status: 'settled',
  walletId: 'w-azul',
  createdAt: '',
  version: 1,
  ...p,
});
const item = (e: Entry): LedgerItem => ({ type: e.kind, entry: e });
const resultado = (p: Partial<SearchResult> = {}): SearchResult => ({
  items: [item(entry('1')), item(entry('2', { date: '2026-02-03' }))],
  totalCents: -5000,
  totalCount: 2,
  nextCursor: null,
  ...p,
});

describe('BuscaPage', () => {
  let http: HttpTestingController;
  let el: HTMLElement;
  let fixture: ReturnType<typeof TestBed.createComponent<BuscaPage>>;

  async function abrir(primeira: SearchResult = resultado()) {
    TestBed.configureTestingModule({
      imports: [BuscaPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BuscaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http
      .expectOne('/api/v1/wallets?archived=all')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde' })]);
    http.expectOne('/api/v1/credit-cards?archived=all').flush([]);
    http.expectOne((r) => r.url.startsWith('/api/v1/entries/search')).flush(primeira);
    await fixture.whenStable();
    fixture.detectChanges();
  }
  const consulta = (req: { request: { urlWithParams: string } }) =>
    Object.fromEntries(new URLSearchParams(req.request.urlWithParams.split('?')[1] ?? ''));
  afterEach(() => http.verify());

  it('lista os resultados de vários meses e o total (US6.5)', async () => {
    await abrir();
    expect(el.querySelectorAll('[data-testid="resultado"]')).toHaveLength(2);
    expect(el.querySelector('[data-testid="total"]')?.textContent).toContain('2 resultados');
    const total = el.querySelector('[data-testid="total-valor"]')!;
    expect(total.textContent).toContain('R$ 50,00');
    expect(total.textContent).toContain('-');
    expect(total.classList).toContain('negativo');
  });

  it('005: resultado que veio de recorrência mostra o ícone e "alterada"', async () => {
    await abrir(
      resultado({
        items: [
          item(entry('1', { description: 'Tim', recurrenceId: 'r-tim' })),
          item(entry('2', { recurrenceId: 'r-tim', recurrenceModified: true })),
          item(entry('3')),
        ],
        totalCount: 3,
      }),
    );
    const linhas = [...el.querySelectorAll('[data-testid="resultado"]')];
    expect(linhas[0].querySelector('[data-testid="selo-recorrencia"]')).not.toBeNull();
    expect(linhas[0].querySelector('[data-testid="selo-alterada"]')).toBeNull();
    expect(linhas[1].querySelector('[data-testid="selo-alterada"]')).not.toBeNull();
    expect(linhas[2].querySelector('[data-testid="selo-recorrencia"]')).toBeNull();
  });

  it('digitar busca depois de uma pausa, enviando o texto', async () => {
    vi.useFakeTimers();
    try {
      await abrir();
      const campo = el.querySelector<HTMLInputElement>('#busca-q')!;
      campo.value = 'uber';
      campo.dispatchEvent(new Event('input'));
      await vi.advanceTimersByTimeAsync(300);
      const req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
      expect(consulta(req)['q']).toBe('uber');
      req.flush(resultado({ items: [item(entry('1'))], totalCents: -2500, totalCount: 1 }));
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="total"]')?.textContent).toContain('1 resultado ·');
    } finally {
      vi.useRealTimers();
    }
  });

  it('filtros de tipo, carteira e situação vão para a API', async () => {
    await abrir();
    el.querySelector<HTMLButtonElement>('[data-testid="tipo-expense"]')!.click();
    let req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
    expect(consulta(req)['kinds']).toBe('expense');
    req.flush(resultado());
    fixture.detectChanges();

    el.querySelectorAll<HTMLButtonElement>('[data-testid="filtro-carteira"]')[1].click();
    req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
    expect(consulta(req)['walletIds']).toBe('w-verde');
    req.flush(resultado());
    fixture.detectChanges();

    const sel = el.querySelector<HTMLSelectElement>('#busca-situacao')!;
    sel.value = 'planned';
    sel.dispatchEvent(new Event('change'));
    req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
    expect(consulta(req)).toMatchObject({
      kinds: 'expense',
      walletIds: 'w-verde',
      status: 'planned',
    });
    req.flush(resultado());
  });

  it('carrega a próxima página pelo cursor e acrescenta à lista', async () => {
    await abrir(resultado({ nextCursor: 'cur-1', totalCount: 3 }));
    el.querySelector<HTMLButtonElement>('[data-testid="carregar-mais"]')!.click();
    const req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
    expect(consulta(req)['cursor']).toBe('cur-1');
    req.flush(resultado({ items: [item(entry('3'))], nextCursor: null, totalCount: 3 }));
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="resultado"]')).toHaveLength(3);
    expect(el.querySelector('[data-testid="carregar-mais"]')).toBeNull();
  });

  it('mostra estado vazio sem resultados', async () => {
    await abrir(resultado({ items: [], totalCents: 0, totalCount: 0 }));
    expect(el.querySelector('[data-testid="busca-vazia"]')).not.toBeNull();
  });

  it('tocar numa saída abre o formulário rápido em edição', async () => {
    await abrir();
    const abrirForm = vi
      .spyOn(TestBed.inject(LancamentoLauncher), 'abrir')
      .mockReturnValue(undefined as never);
    el.querySelector<HTMLButtonElement>('[data-testid="resultado"] button')!.click();
    expect(abrirForm).toHaveBeenCalledWith(expect.objectContaining({ id: '1' }));
  });

  it('exclui uma transferência com Desfazer e refaz a busca', async () => {
    await abrir(
      resultado({
        items: [
          {
            type: 'transfer',
            transfer: {
              id: 't1',
              fromWalletId: 'w-azul',
              toWalletId: 'w-verde',
              amountCents: 10000,
              date: '2026-03-08',
              description: 'Transferência',
              createdAt: '',
              version: 1,
            },
          },
        ],
        totalCents: 0,
        totalCount: 1,
      }),
    );
    const snack = vi
      .spyOn(TestBed.inject(Avisos), 'open')
      .mockReturnValue({ onAction: () => new Subject<void>() } as never);
    expect(el.querySelector('[data-testid="resultado-transferencia"]')?.textContent).toContain(
      'Azul → Verde',
    );
    el.querySelector<HTMLButtonElement>('[data-testid="excluir-transferencia"]')!.click();
    http.expectOne('/api/v1/transfers/t1').flush({ deletionBatchId: 'lote-3' });
    http.expectOne((r) => r.url.startsWith('/api/v1/entries/search')).flush(resultado());
    expect(snack).toHaveBeenCalledWith('Excluído', 'Desfazer', expect.anything());
  });

  it('mostra o erro da busca', async () => {
    TestBed.configureTestingModule({
      imports: [BuscaPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BuscaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([]);
    http.expectOne('/api/v1/credit-cards?archived=all').flush([]);
    http
      .expectOne((r) => r.url.startsWith('/api/v1/entries/search'))
      .flush({ code: 'x', detail: 'Falhou.' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-busca"]')?.textContent).toContain('Falhou.');
  });

  describe('tags e compras no cartão (004 FR-004)', () => {
    const compraItem = (): LedgerItem => ({
      type: 'card_purchase',
      purchase: {
        id: 'p1',
        creditCardId: 'c-azul',
        dueMonth: '2026-03',
        description: 'Ifood',
        displayDescription: 'Ifood',
        amountCents: 15681,
        nature: 'debit',
        date: '2026-03-04',
        origin: 'manual',
        reconciled: false,
        manuallyMoved: false,
        createdAt: '',
        version: 1,
      },
    });

    it('filtra por tags e lista as compras do cartão com o nome do cartão', async () => {
      await abrir();
      el.querySelector<HTMLElement>('[data-testid="filtro-tags-abrir"]')!.click();
      http.expectOne('/api/v1/tags?archived=all').flush([bruna]);
      await fixture.whenStable();
      fixture.detectChanges();
      el.querySelector<HTMLElement>('[data-testid="filtro-tag"]')!.click();
      await fixture.whenStable();

      const req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
      expect(consulta(req)['tagIds']).toBe('t-bruna');
      req.flush(resultado({ items: [item(entry('1')), compraItem()], totalCount: 2 }));
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelectorAll('[data-testid="resultado"]')).toHaveLength(1);
      const compra = el.querySelector('[data-testid="resultado-compra"]')!;
      expect(compra.textContent).toContain('Ifood');
      expect(compra.textContent).toContain('Compra no cartão');
      expect(compra.textContent).toContain('R$ 156,81');
      expect(el.querySelector('[data-testid="limpar"]')).not.toBeNull();
    });

    it('tocar numa compra abre o formulário de edição da compra', async () => {
      await abrir(resultado({ items: [compraItem()], totalCount: 1 }));
      const abrirCompra = vi
        .spyOn(TestBed.inject(LancamentoLauncher), 'abrirCompra')
        .mockReturnValue(undefined as never);
      el.querySelector<HTMLElement>('[data-testid="resultado-compra"] button')!.click();
      expect(abrirCompra).toHaveBeenCalledWith(expect.objectContaining({ id: 'p1' }));
    });

    it('o tipo "Compras no cartão" vai em kinds', async () => {
      await abrir();
      el.querySelector<HTMLElement>('[data-testid="tipo-card_purchase"]')!.click();
      await fixture.whenStable();
      const req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
      expect(consulta(req)['kinds']).toBe('card_purchase');
      req.flush(resultado());
    });
  });
});
