import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject } from 'rxjs';
import { carteira } from '../carteiras/testing';
import { compra } from '../cartoes/fatura/testing';
import { cartao } from '../cartoes/testing';
import { recorrencia } from '../recorrencias/testing';
import { CardPurchase } from './card-purchase.models';
import { EntriesService } from './entries.service';
import { Entry } from './entry.models';
import { LancamentoSheet } from './lancamento.sheet';

const seguro: Entry = {
  id: 'e1',
  kind: 'expense',
  description: 'Seguro Vida',
  amountCents: 2599,
  date: '2026-03-25',
  referenceMonth: '2026-03',
  status: 'planned',
  walletId: 'w-azul',
  createdAt: '',
  version: 1,
};

describe('LancamentoSheet: Repetir todo mês (005 US4)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function abrir(dados: { entry?: Entry; purchase?: CardPurchase }) {
    fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: fechar } },
        { provide: MAT_DIALOG_DATA, useValue: dados },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({
      onAction: () => new Subject<void>(),
    } as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    http.match('/api/v1/tags?archived=all').forEach((req) => req.flush([]));
    http.match('/api/v1/credit-cards?archived=all').forEach((req) => req.flush([cartao()]));
    await atualizar();
  }
  const q = (id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const clicar = async (id: string) => {
    q(id)!.click();
    await atualizar();
  };
  const escolher = async (id: string, valor: string) => {
    const campo = q(id) as HTMLSelectElement | HTMLInputElement;
    campo.value = valor;
    campo.dispatchEvent(new Event(campo instanceof HTMLSelectElement ? 'change' : 'input'));
    await atualizar();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    http.verify();
  });

  it('oferece "Repetir todo mês" só na edição', async () => {
    await abrir({ entry: seguro });
    expect(q('repetir')).not.toBeNull();
  });

  it('não oferece em lançamento que já é ocorrência de recorrência', async () => {
    await abrir({ entry: { ...seguro, recurrenceId: 'r-tim', recurrenceMonth: '2026-03' } });
    expect(q('repetir')).toBeNull();
  });

  it('repete sem fim: envia o lançamento, fecha e avisa as telas (US4.1)', async () => {
    await abrir({ entry: seguro });
    const alterados: Entry[] = [];
    TestBed.inject(EntriesService).alterados$.subscribe((e) => alterados.push(e));
    await clicar('repetir');
    expect(q('repetir-painel')).not.toBeNull();
    await clicar('repetir-confirmar');
    const req = http.expectOne('/api/v1/recurrences/from-item');
    expect(req.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(req.request.body).toEqual({ type: 'entry', id: 'e1', endType: 'none' });
    req.flush(recorrencia({ description: 'Seguro Vida' }), { status: 201, statusText: 'Created' });
    expect(fechar).toHaveBeenCalledWith(seguro);
    expect(alterados).toEqual([seguro]);
  });

  it('repete até um mês e após N ocorrências', async () => {
    await abrir({ entry: seguro });
    await clicar('repetir');
    await escolher('repetir-fim', 'until_month');
    await escolher('repetir-mes', '2026-12');
    await clicar('repetir-confirmar');
    const ate = http.expectOne('/api/v1/recurrences/from-item');
    expect(ate.request.body).toEqual({
      type: 'entry',
      id: 'e1',
      endType: 'until_month',
      endMonth: '2026-12',
    });
    ate.flush({ code: 'x', title: 'x', detail: 'Falhou.' }, { status: 500, statusText: 'Erro' });
    await atualizar();

    await escolher('repetir-fim', 'after_count');
    await escolher('repetir-qtd', '6');
    await clicar('repetir-confirmar');
    const apos = http.expectOne('/api/v1/recurrences/from-item');
    expect(apos.request.body).toEqual({
      type: 'entry',
      id: 'e1',
      endType: 'after_count',
      occurrenceLimit: 6,
    });
    apos.flush(recorrencia(), { status: 201, statusText: 'Created' });
  });

  it('valida o término antes de enviar', async () => {
    await abrir({ entry: seguro });
    await clicar('repetir');
    await escolher('repetir-fim', 'until_month');
    await escolher('repetir-mes', '2026-01');
    await clicar('repetir-confirmar');
    expect(q('erro-repetir')!.textContent).toContain('mês de término');
    await escolher('repetir-fim', 'after_count');
    await escolher('repetir-qtd', '0');
    await clicar('repetir-confirmar');
    expect(q('erro-repetir')!.textContent).toContain('ocorrências');
  });

  it('mostra o erro 422 do servidor e mantém o painel aberto', async () => {
    await abrir({ entry: seguro });
    await clicar('repetir');
    await clicar('repetir-confirmar');
    http.expectOne('/api/v1/recurrences/from-item').flush(
      {
        code: 'recurrence.item_not_eligible',
        title: 'Não elegível',
        detail:
          'Só Entrada, Saída ou Compra à vista que ainda não é recorrência pode se repetir todo mês',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await atualizar();
    expect(q('erro-repetir')!.textContent).toContain('pode se repetir todo mês');
    expect(fechar).not.toHaveBeenCalled();
  });

  it('compra à vista envia card_purchase; parcelada ou crédito não oferece', async () => {
    await abrir({ purchase: compra() });
    await clicar('repetir');
    await clicar('repetir-confirmar');
    const req = http.expectOne('/api/v1/recurrences/from-item');
    expect(req.request.body).toMatchObject({ type: 'card_purchase', endType: 'none' });
    req.flush(recorrencia({ kind: 'card_purchase' }), { status: 201, statusText: 'Created' });
    fixture.destroy();
    TestBed.resetTestingModule();

    await abrir({ purchase: compra({ installmentPlanId: 'p1', installmentTotal: 3 }) });
    expect(q('repetir')).toBeNull();
    fixture.destroy();
    TestBed.resetTestingModule();

    await abrir({ purchase: compra({ nature: 'credit' }) });
    expect(q('repetir')).toBeNull();
  });
});
