import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { carteira } from '../carteiras/testing';
import { Entry } from '../lancamento/entry.models';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { MonthView, SummaryColumn } from './month.models';
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
});
