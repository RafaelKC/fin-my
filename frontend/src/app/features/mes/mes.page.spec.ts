import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { RouterTestingHarness } from '@angular/router/testing';
import { carteira } from '../carteiras/testing';
import { cartao } from '../cartoes/testing';
import { bruna, ifood } from '../../shared/tag-picker/testing';
import { CardPurchase } from '../lancamento/card-purchase.models';
import { CardPurchasesService } from '../lancamento/card-purchases.service';
import { Entry } from '../lancamento/entry.models';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { TransfersService } from '../lancamento/transfers.service';
import { MonthInvoiceLine, MonthView, SummaryColumn, Transfer } from './month.models';
import { MesPage } from './mes.page';

const col = (p: Partial<SummaryColumn>): SummaryColumn => ({
  openingBalanceCents: 50000,
  walletsInitialBalanceCents: 50000,
  incomeCents: 0,
  expenseCents: 0,
  resultCents: 0,
  transfersInCents: 0,
  transfersOutCents: 0,
  finalBalanceCents: 0,
  ...p,
});
const entry = (p: Partial<Entry>): Entry => ({
  id: 'e',
  kind: 'income',
  description: 'Salário',
  amountCents: 150000,
  date: '2026-03-05',
  referenceMonth: '2026-03',
  status: 'settled',
  walletId: 'w-azul',
  createdAt: '',
  version: 1,
  ...p,
});
const transferencia = (p: Partial<Transfer> = {}): Transfer => ({
  id: 't1',
  fromWalletId: 'w-azul',
  toWalletId: 'w-verde',
  amountCents: 2000,
  date: '2026-03-12',
  description: 'Transferência',
  createdAt: '',
  version: 1,
  ...p,
});
/** Exemplo da US5: fatura Azul de março com Total 200,00, Fechada, hoje 20/03/2026. */
const faturaDoMes = (p: Partial<MonthInvoiceLine> = {}): MonthInvoiceLine => ({
  creditCardId: 'c-azul',
  creditCardName: 'Azul',
  dueMonth: '2026-03',
  closingDate: '2026-03-18',
  dueDate: '2026-03-25',
  id: 'f1',
  status: 'closed',
  totalCents: 20000,
  paidCents: 0,
  remainingCents: 20000,
  plannedCents: 20000,
  actualCents: 0,
  ...p,
});
const visao = (p: Partial<MonthView> = {}): MonthView => ({
  month: '2026-03',
  today: '2026-03-10',
  walletIds: null,
  planned: col({
    incomeCents: 151827,
    expenseCents: 81502,
    resultCents: 70325,
    finalBalanceCents: 120325,
  }),
  actual: col({
    incomeCents: 150000,
    expenseCents: 4499,
    resultCents: 145501,
    finalBalanceCents: 195501,
  }),
  entries: [
    entry({ id: '1' }),
    entry({
      id: '2',
      description: 'Duda',
      amountCents: 1827,
      status: 'planned',
      date: '2026-03-02',
    }),
    entry({
      id: '3',
      kind: 'expense',
      description: 'Léo Casa',
      amountCents: 77003,
      date: '2026-03-20',
      status: 'planned',
    }),
  ],
  transfers: [],
  pending: { count: 0, toReceiveCents: 0, toPayCents: 0 },
  ...p,
});

describe('MesPage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  async function abrir(url = '/mes/2026-03', dados: MonthView = visao()) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'mes/:mes', component: MesPage },
          { path: 'cartoes/:id/faturas/:mes', children: [] },
        ]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    const nav = harness.navigateByUrl(url);
    await nav;
    http
      .expectOne('/api/v1/wallets?archived=all')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde' })]);
    http.expectOne('/api/v1/credit-cards?archived=all').flush([cartao()]);
    http.expectOne((r) => r.url.startsWith('/api/v1/months/')).flush(dados);
    harness.detectChanges();
    el = harness.routeNativeElement as HTMLElement;
  }
  const txt = (id: string) =>
    el.querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();
  afterEach(() => http.verify());

  it('mostra o resumo e o saldo inicial de carteiras (US3.1)', async () => {
    await abrir();
    expect(el.querySelector('h1')?.textContent).toContain('Março de 2026');
    expect(txt('resumo-saldo-inicial')).toContain('inclui saldo inicial de carteiras');
    expect(
      el.querySelector('[data-testid="resumo-entradas"] [data-testid="previsto"]')?.textContent,
    ).toContain('R$ 1.518,27');
    expect(
      el.querySelector('[data-testid="resumo-saldo-final"] [data-testid="realizado"]')?.textContent,
    ).toContain('R$ 1.955,01');
  });

  it('destaca negativos em vermelho (US3.4)', async () => {
    await abrir(
      '/mes/2026-03',
      visao({ planned: col({ resultCents: -500, finalBalanceCents: 100 }) }),
    );
    expect(
      el.querySelector('[data-testid="resumo-resultado"] [data-testid="previsto"]')?.classList,
    ).toContain('negativo');
    expect(
      el.querySelector('[data-testid="resumo-saldo-final"] [data-testid="previsto"]')?.classList,
    ).not.toContain('negativo');
  });

  it('marca "Atrasada" só na prevista com data anterior a hoje', async () => {
    await abrir();
    const linhas = [...el.querySelectorAll('[data-testid="lancamento"]')];
    expect(linhas).toHaveLength(3);
    expect(el.querySelectorAll('[data-testid="atrasada"]')).toHaveLength(1);
    expect(linhas[1].textContent).toContain('Atrasada');
  });

  it('a caixinha faz PATCH e recarrega o resumo (US3.2)', async () => {
    await abrir();
    const caixa = el.querySelectorAll<HTMLInputElement>('[data-testid="situacao"]')[1];
    caixa.click();
    const patch = http.expectOne('/api/v1/entries/2/status');
    expect(patch.request.body).toEqual({ status: 'settled' });
    patch.flush(entry({ id: '2', status: 'settled' }));
    http.expectOne('/api/v1/months/2026-03').flush(visao());
  });

  it('filtro de situação lista só as previstas e vai à query string', async () => {
    await abrir('/mes/2026-03?situacao=planned');
    expect(el.querySelectorAll('[data-testid="lancamento"]')).toHaveLength(2);
  });

  it('mês vazio mostra o convite para lançar', async () => {
    await abrir('/mes/2026-04', visao({ month: '2026-04', entries: [] }));
    expect(txt('mes-vazio')).toBe('Nada lançado neste mês. Toque em + para lançar.');
  });

  it('setas mantêm os filtros e o deslizar navega', async () => {
    await abrir('/mes/2026-03?carteiras=w-azul&situacao=planned');
    const router = TestBed.inject(Router);
    el.querySelector<HTMLButtonElement>('[data-testid="mes-seguinte"]')!.click();
    await harness.fixture.whenStable();
    expect(router.url).toBe('/mes/2026-04?carteiras=w-azul&situacao=planned');
    http.expectOne('/api/v1/months/2026-04?walletIds=w-azul').flush(visao({ month: '2026-04' }));
  });

  it('tocar no item abre o formulário em edição', async () => {
    await abrir();
    const abrirForm = vi
      .spyOn(TestBed.inject(LancamentoLauncher), 'abrir')
      .mockReturnValue(undefined as never);
    el.querySelector<HTMLButtonElement>('[data-testid="lancamento"] .linha')!.click();
    expect(abrirForm).toHaveBeenCalledWith(expect.objectContaining({ id: '1' }));
  });

  it('mostra o aviso de pendências só no mês de hoje (US4.1)', async () => {
    const pending = { count: 1, toReceiveCents: 30000, toPayCents: 0 };
    await abrir('/mes/2026-03', visao({ pending }));
    expect(txt('pendencias-aviso')).toContain('1 pendência de meses anteriores');
    expect(txt('pendencias-receber')).toBe('R$ 300,00');
  });

  it('não mostra o aviso em outro mês nem sem pendências', async () => {
    await abrir(
      '/mes/2026-04',
      visao({ month: '2026-04', pending: { count: 1, toReceiveCents: 30000, toPayCents: 0 } }),
    );
    expect(el.querySelector('[data-testid="pendencias"]')).toBeNull();
  });

  it('não mostra o aviso quando não há pendências', async () => {
    await abrir();
    expect(el.querySelector('[data-testid="pendencias"]')).toBeNull();
  });

  describe('transferências (US5)', () => {
    const abrirLista = () => {
      el.querySelector<HTMLButtonElement>('[data-testid="transferencias-alternar"]')!.click();
      harness.detectChanges();
    };

    it('lista recolhida por padrão; sem filtro mostra "Transferência" e De → Para', async () => {
      await abrir('/mes/2026-03', visao({ transfers: [transferencia()] }));
      expect(el.querySelector('[data-testid="lista-transferencias"]')).toBeNull();
      expect(txt('transferencias-alternar')).toContain('Transferências (1)');
      abrirLista();
      expect(txt('transferencia')).toContain('Azul → Verde');
      expect(txt('transferencia-rotulo')).toBe('Transferência');
      // todas as carteiras: as transferências se anulam e o resumo não ganha linhas
      expect(el.querySelector('[data-testid="resumo-transferencias-enviadas"]')).toBeNull();
    });

    it('filtro de uma carteira: "Transferência enviada" e linhas no resumo (US5.2)', async () => {
      await abrir(
        '/mes/2026-03?carteiras=w-azul',
        visao({
          walletIds: ['w-azul'],
          actual: col({ transfersOutCents: 2000, finalBalanceCents: 48000 }),
          transfers: [transferencia()],
        }),
      );
      abrirLista();
      expect(txt('transferencia-rotulo')).toBe('Transferência enviada');
      expect(
        el.querySelector('[data-testid="resumo-transferencias-enviadas"] [data-testid="realizado"]')
          ?.textContent,
      ).toContain('R$ 20,00');
      expect(txt('resumo-transferencias-recebidas')).toContain('Transferências recebidas');
    });

    it('filtro pela carteira de destino: "Transferência recebida"', async () => {
      await abrir(
        '/mes/2026-03?carteiras=w-verde',
        visao({ walletIds: ['w-verde'], transfers: [transferencia()] }),
      );
      abrirLista();
      expect(txt('transferencia-rotulo')).toBe('Transferência recebida');
    });

    it('recarrega a visão quando uma transferência é criada (FR-016)', async () => {
      await abrir();
      TestBed.inject(TransfersService)
        .create(
          { fromWalletId: 'w-azul', toWalletId: 'w-verde', amount: '20', date: '2026-03-12' },
          'k',
        )
        .subscribe();
      http.expectOne('/api/v1/transfers').flush(transferencia());
      http.expectOne('/api/v1/months/2026-03').flush(visao({ transfers: [transferencia()] }));
      http.expectOne('/api/v1/wallets?archived=all').flush([]);
      harness.detectChanges();
      expect(txt('transferencias-alternar')).toContain('Transferências (1)');
    });
  });

  it('exclui uma transferência e recarrega o mês, oferecendo Desfazer (RT-09)', async () => {
    await abrir(
      '/mes/2026-03',
      visao({
        transfers: [
          {
            id: 't1',
            fromWalletId: 'w-azul',
            toWalletId: 'w-verde',
            amountCents: 10000,
            date: '2026-03-08',
            description: 'Transferência',
            createdAt: '',
            version: 1,
          },
        ],
      }),
    );
    const snack = vi
      .spyOn(TestBed.inject(MatSnackBar), 'open')
      .mockReturnValue({ onAction: () => new Subject<void>() } as never);
    el.querySelector<HTMLButtonElement>('[data-testid="transferencias-alternar"]')!.click();
    harness.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid="excluir-transferencia"]')!.click();
    const del = http.expectOne('/api/v1/transfers/t1');
    expect(del.request.method).toBe('DELETE');
    del.flush({ deletionBatchId: 'lote-7' });
    http.expectOne((r) => r.url.startsWith('/api/v1/months/')).flush(visao());
    expect(snack).toHaveBeenCalledWith(
      'Excluído',
      'Desfazer',
      expect.objectContaining({ duration: 8000 }),
    );
  });

  describe('faturas (US5.1, US5.6; FR-019, FR-021)', () => {
    const caixa = () => el.querySelector<HTMLInputElement>('[data-testid="fatura-caixinha"]')!;

    it('lista "Fatura <Cartão>" nas Saídas com situação, Total, Pago e Restante', async () => {
      await abrir(
        '/mes/2026-03',
        visao({
          invoices: [faturaDoMes({ paidCents: 15000, remainingCents: 5000, actualCents: 15000 })],
        }),
      );
      const linha = txt('fatura-mes')!;
      expect(linha).toContain('Fatura Azul');
      expect(txt('fatura-situacao')).toBe('Fechada');
      expect(txt('fatura-total')).toBe('R$ 200,00');
      expect(txt('fatura-pago')).toBe('R$ 150,00');
      expect(txt('fatura-restante')).toBe('R$ 50,00');
      expect(caixa().checked).toBe(false);
    });

    it('mês só com fatura não é "vazio"', async () => {
      await abrir('/mes/2026-03', visao({ entries: [], invoices: [faturaDoMes()] }));
      expect(el.querySelector('[data-testid="mes-vazio"]')).toBeNull();
      expect(el.querySelector('[data-testid="fatura-mes"]')).not.toBeNull();
    });

    it('marcar a caixinha paga com os padrões (corpo vazio) e recarrega', async () => {
      await abrir('/mes/2026-03', visao({ invoices: [faturaDoMes()] }));
      caixa().click();
      const post = http.expectOne('/api/v1/credit-cards/c-azul/invoices/2026-03/payments');
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual({});
      expect(post.request.headers.get('Idempotency-Key')).toBeTruthy();
      post.flush({});
      http.expectOne('/api/v1/months/2026-03').flush(
        visao({
          invoices: [
            faturaDoMes({
              status: 'paid',
              paidCents: 20000,
              remainingCents: 0,
              actualCents: 20000,
            }),
          ],
        }),
      );
      harness.detectChanges();
      expect(txt('fatura-situacao')).toBe('Paga');
      expect(caixa().checked).toBe(true);
    });

    it('desmarcar exclui os pagamentos e oferece Desfazer (US5.6)', async () => {
      await abrir(
        '/mes/2026-03',
        visao({
          invoices: [
            faturaDoMes({
              status: 'paid',
              paidCents: 20000,
              remainingCents: 0,
              actualCents: 20000,
            }),
          ],
        }),
      );
      const snack = vi
        .spyOn(TestBed.inject(MatSnackBar), 'open')
        .mockReturnValue({ onAction: () => new Subject<void>() } as never);
      expect(caixa().checked).toBe(true);
      caixa().click();
      const del = http.expectOne('/api/v1/credit-cards/c-azul/invoices/2026-03/payments');
      expect(del.request.method).toBe('DELETE');
      del.flush({ deletionBatchId: 'lote-3' });
      http.expectOne('/api/v1/months/2026-03').flush(visao({ invoices: [faturaDoMes()] }));
      expect(snack).toHaveBeenCalledWith(
        'Excluído',
        'Desfazer',
        expect.objectContaining({ duration: 8000 }),
      );
    });

    it('erro ao pagar mostra o aviso e recarrega a marcação', async () => {
      await abrir('/mes/2026-03', visao({ invoices: [faturaDoMes()] }));
      caixa().click();
      http
        .expectOne('/api/v1/credit-cards/c-azul/invoices/2026-03/payments')
        .flush(
          { code: 'invoice.nothing_to_pay', title: 'x', detail: 'Não há valor a pagar.' },
          { status: 422, statusText: 'Unprocessable Entity' },
        );
      http.expectOne('/api/v1/months/2026-03').flush(visao({ invoices: [faturaDoMes()] }));
      harness.detectChanges();
      expect(el.textContent).toContain('Não há valor a pagar.');
    });

    it('fatura zerada e sem pagamento não pode ser marcada', async () => {
      await abrir(
        '/mes/2026-03',
        visao({
          invoices: [
            faturaDoMes({
              status: 'no_amount_due',
              totalCents: 0,
              remainingCents: 0,
              plannedCents: 0,
            }),
          ],
        }),
      );
      expect(caixa().disabled).toBe(true);
    });

    it('tocar na linha abre a fatura', async () => {
      await abrir('/mes/2026-03', visao({ invoices: [faturaDoMes()] }));
      el.querySelector<HTMLButtonElement>('[data-testid="fatura-abrir"]')!.click();
      await harness.fixture.whenStable();
      expect(TestBed.inject(Router).url).toBe('/cartoes/c-azul/faturas/2026-03');
    });

    it('filtro Previstas esconde a fatura paga; Realizadas esconde a aberta', async () => {
      const paga = faturaDoMes({ status: 'paid', paidCents: 20000, remainingCents: 0 });
      await abrir('/mes/2026-03?situacao=planned', visao({ invoices: [paga] }));
      expect(el.querySelector('[data-testid="fatura-mes"]')).toBeNull();
    });

    it('filtro Realizadas mostra só a fatura paga', async () => {
      const paga = faturaDoMes({ status: 'paid', paidCents: 20000, remainingCents: 0 });
      await abrir(
        '/mes/2026-03?situacao=settled',
        visao({
          invoices: [
            paga,
            faturaDoMes({ dueMonth: '2026-03', creditCardId: 'c-verde', creditCardName: 'Verde' }),
          ],
        }),
      );
      expect(el.querySelectorAll('[data-testid="fatura-mes"]')).toHaveLength(1);
      expect(txt('fatura-mes')).toContain('Fatura Azul');
    });

    it('recarrega a visão quando uma compra no cartão é editada', async () => {
      await abrir('/mes/2026-03', visao({ invoices: [faturaDoMes()] }));
      TestBed.inject(CardPurchasesService).delete('p1').subscribe();
      http.expectOne('/api/v1/card-purchases/p1?scope=this').flush({ deletionBatchId: 'l' });
      http.expectOne('/api/v1/months/2026-03').flush(visao());
    });
  });
  describe('filtro por tag (004 US1.5, FR-004)', () => {
    const compraDoFiltro = (p: Partial<CardPurchase> = {}): CardPurchase => ({
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
      tagIds: ['t-bruna'],
      settlementTagId: 't-bruna',
      ...p,
    });
    const filtrada = (): MonthView =>
      visao({
        entries: [
          entry({ id: 'x1', kind: 'expense', description: 'Presente Bruna', amountCents: 4000 }),
        ],
        purchases: [compraDoFiltro()],
        filterTotals: { incomeCents: 0, expenseCents: 4000, purchasesNetCents: 15681 },
      });

    it('manda tagIds da query à API e mostra lançamentos, compras e os totais do filtro', async () => {
      await abrir('/mes/2026-03?tags=t-bruna', filtrada());
      expect(el.querySelectorAll('[data-testid="lancamento"]')).toHaveLength(1);
      expect(txt('lista-compras-filtro')).toContain('Ifood');
      expect(txt('lista-compras-filtro')).toContain('R$ 156,81');
      expect(txt('lista-compras-filtro')).toContain('Azul');
      expect(txt('filtro-saidas')).toBe('R$ 40,00');
      expect(txt('filtro-compras')).toBe('R$ 156,81');
      // o resumo continua o do mês inteiro (research R6)
      expect(
        el.querySelector('[data-testid="resumo-entradas"] [data-testid="previsto"]')?.textContent,
      ).toContain('R$ 1.518,27');
      expect(el.querySelector('[data-testid="filtro-tags-abrir"]')?.textContent).toContain(
        'Tags (1)',
      );
    });

    it('escolher uma tag no filtro refaz a consulta com tagIds e guarda na URL', async () => {
      await abrir();
      el.querySelector<HTMLElement>('[data-testid="filtro-tags-abrir"]')!.click();
      http.expectOne('/api/v1/tags?archived=all').flush([bruna, ifood]);
      harness.detectChanges();
      await harness.fixture.whenStable();
      harness.detectChanges();

      el.querySelector<HTMLElement>('[data-testid="filtro-tag"][data-tag="Bruna"]')!.click();
      await harness.fixture.whenStable();
      http.expectOne('/api/v1/months/2026-03?tagIds=t-bruna').flush(filtrada());
      harness.detectChanges();
      expect(TestBed.inject(Router).url).toContain('tags=t-bruna');
      expect(el.querySelector('[data-testid="totais-filtro"]')).not.toBeNull();
    });

    it('sem resultado mostra a mensagem do filtro e nada de "Nada lançado"', async () => {
      await abrir(
        '/mes/2026-03?tags=t-bruna',
        visao({
          entries: [],
          purchases: [],
          filterTotals: { incomeCents: 0, expenseCents: 0, purchasesNetCents: 0 },
        }),
      );
      expect(el.querySelector('[data-testid="filtro-tags-sem-resultado"]')).not.toBeNull();
      expect(el.querySelector('[data-testid="mes-vazio"]')).toBeNull();
    });

    it('sem filtro de tags não mostra totais nem compras', async () => {
      await abrir();
      expect(el.querySelector('[data-testid="totais-filtro"]')).toBeNull();
      expect(el.querySelector('[data-testid="lista-compras-filtro"]')).toBeNull();
    });

    it('tocar numa compra do filtro abre o formulário em edição', async () => {
      await abrir('/mes/2026-03?tags=t-bruna', filtrada());
      const abrirCompra = vi
        .spyOn(TestBed.inject(LancamentoLauncher), 'abrirCompra')
        .mockReturnValue(undefined as never);
      el.querySelector<HTMLElement>('[data-testid="compra-filtro"] button')!.click();
      expect(abrirCompra).toHaveBeenCalledWith(expect.objectContaining({ id: 'p1' }));
    });
  });
});
