import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { Entry } from '../../lancamento/entry.models';
import { carteira } from '../../carteiras/testing';
import { DESFAZER_MS, PendenciasComponent } from './pendencias.component';
import { Avisos } from '@/shared/ui/avisos';

const pend = (p: Partial<Entry>): Entry => ({
  id: 'p1',
  kind: 'income',
  description: 'Carla atrasado',
  amountCents: 30000,
  date: '2026-02-10',
  referenceMonth: '2026-02',
  status: 'planned',
  walletId: 'w-azul',
  createdAt: '',
  version: 1,
  ...p,
});
const lista = [
  pend({}),
  pend({ id: 'p2', kind: 'expense', description: 'Léo Casa', amountCents: 12050 }),
];

describe('PendenciasComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<PendenciasComponent>;
  let el: HTMLElement;
  const acao = new Subject<void>();
  const snack = {
    open: vi.fn(() => ({ onAction: () => acao.asObservable() })),
  };

  const q = (id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`)!;
  const qa = (id: string) => [...el.querySelectorAll<HTMLInputElement>(`[data-testid="${id}"]`)];

  async function ciclo() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function montar(resumo = { count: 2, toReceiveCents: 30000, toPayCents: 12050 }) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Avisos, useValue: snack },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PendenciasComponent);
    fixture.componentRef.setInput('resumo', resumo);
    fixture.componentRef.setInput('carteiras', [carteira()]);
    el = fixture.nativeElement as HTMLElement;
    await ciclo();
  }
  async function abrirLista() {
    q('pendencias-alternar').click();
    http.expectOne('/api/v1/entries/pending').flush(lista);
    await ciclo();
  }
  afterEach(() => {
    http.verify();
    snack.open.mockClear();
  });

  it('mostra a contagem e os totais a receber e a pagar (US4.1)', async () => {
    await montar();
    expect(q('pendencias-aviso').textContent).toContain('2 pendências de meses anteriores');
    expect(q('pendencias-receber').textContent).toContain('R$ 300,00');
    expect(q('pendencias-pagar').textContent).toContain('R$ 120,50');
  });

  it('usa o singular com uma pendência', async () => {
    await montar({ count: 1, toReceiveCents: 30000, toPayCents: 0 });
    expect(q('pendencias-aviso').textContent).toContain('1 pendência de meses anteriores');
  });

  it('lista as pendências só depois de abrir e muda o rótulo de realizar pela seleção', async () => {
    await montar();
    expect(el.querySelector('[data-testid="pendencias-lista"]')).toBeNull();
    await abrirLista();
    expect(el.querySelectorAll('[data-testid="pendencia"]')).toHaveLength(2);
    expect(q('pendencias-realizar').hasAttribute('disabled')).toBe(true);

    qa('pendencia-selecionar')[0].click();
    await ciclo();
    expect(q('pendencias-realizar').textContent).toContain('Marcar como recebida');
    qa('pendencia-selecionar')[1].click();
    await ciclo();
    expect(q('pendencias-realizar').textContent).toContain('Marcar como recebida/paga');
  });

  it('"Selecionar todas" marca e desmarca tudo', async () => {
    await montar();
    await abrirLista();
    q('pendencias-todas').click();
    await ciclo();
    expect(qa('pendencia-selecionar').every((c) => c.checked)).toBe(true);
    q('pendencias-todas').click();
    await ciclo();
    expect(qa('pendencia-selecionar').every((c) => !c.checked)).toBe(true);
  });

  it('"Marcar como recebida/paga" e "Trazer para este mês" mandam o lote (US4.2, US4.3)', async () => {
    await montar();
    await abrirLista();
    q('pendencias-todas').click();
    await ciclo();

    q('pendencias-realizar').click();
    const a = http.expectOne('/api/v1/entries/bulk');
    expect(a.request.body).toEqual({ ids: ['p1', 'p2'], action: 'settle' });
    a.flush({ affected: 2, deletionBatchId: null });
    await ciclo();

    q('pendencias-todas').click();
    await ciclo();
    q('pendencias-trazer').click();
    const b = http.expectOne('/api/v1/entries/bulk');
    expect(b.request.body).toEqual({ ids: ['p1', 'p2'], action: 'move_to_current_month' });
    b.flush({ affected: 2, deletionBatchId: null });
    expect(snack.open).not.toHaveBeenCalled();
  });

  it('excluir oferece "Desfazer" por 8 s e desfazer restaura o lote (US4.4)', async () => {
    await montar();
    await abrirLista();
    qa('pendencia-selecionar')[0].click();
    await ciclo();
    q('pendencias-excluir').click();
    http.expectOne('/api/v1/entries/bulk').flush({ affected: 1, deletionBatchId: 'lote-1' });

    expect(snack.open).toHaveBeenCalledWith(
      'Lançamento excluído.',
      'Desfazer',
      expect.objectContaining({ duration: DESFAZER_MS }),
    );
    expect(DESFAZER_MS).toBe(8000);

    acao.next();
    const restaurar = http.expectOne('/api/v1/deletions/lote-1/restore');
    expect(restaurar.request.method).toBe('POST');
    restaurar.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('erro do lote mostra a mensagem e recarrega a lista (tudo ou nada)', async () => {
    await montar();
    await abrirLista();
    qa('pendencia-selecionar')[0].click();
    await ciclo();
    q('pendencias-excluir').click();
    http
      .expectOne('/api/v1/entries/bulk')
      .flush(
        { code: 'not_found', title: 'Lançamento não encontrado.' },
        { status: 404, statusText: 'x' },
      );
    http.expectOne('/api/v1/entries/pending').flush(lista);
    await ciclo();
    expect(q('pendencias-erro')).not.toBeNull();
  });
});
