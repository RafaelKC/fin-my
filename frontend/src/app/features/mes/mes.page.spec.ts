import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { RouterTestingHarness } from '@angular/router/testing';
import { carteira } from '../carteiras/testing';
import { Entry } from '../lancamento/entry.models';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { TransfersService } from '../lancamento/transfers.service';
import { MonthView, SummaryColumn, Transfer } from './month.models';
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
        provideRouter([{ path: 'mes/:mes', component: MesPage }]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    const nav = harness.navigateByUrl(url);
    await nav;
    http
      .expectOne('/api/v1/wallets?archived=all')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde' })]);
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
});
