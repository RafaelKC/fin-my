import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { carteira } from '../carteiras/testing';
import { compra } from '../cartoes/fatura/testing';
import { cartao } from '../cartoes/testing';
import { CardPurchase } from './card-purchase.models';
import { LancamentoSheet } from './lancamento.sheet';
import { DADOS_DIALOGO, Dialogo, DialogoRef } from '@/shared/ui/dialogo';
import { Avisos } from '@/shared/ui/avisos';

const azul = cartao();
const verde = cartao({ id: 'c-verde', name: 'Verde', closingDay: 1, dueDay: 10 });

describe('LancamentoSheet — editar compra no cartão (003 T009, FR-015)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function abrir(purchase: CardPurchase = compra({ notes: 'Corrida' })) {
    fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DialogoRef, useValue: { close: fechar } },
        { provide: DADOS_DIALOGO, useValue: { purchase } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Avisos), 'open').mockReturnValue({
      onAction: () => new Subject<void>(),
    } as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    http.match('/api/v1/tags?archived=all').forEach((req) => req.flush([]));
    http.expectOne('/api/v1/credit-cards?archived=all').flush([verde, azul]);
    await atualizar();
  }
  const digitar = async (seletor: string, valor: string) => {
    const campo = el.querySelector<HTMLInputElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  };
  const salvar = async () => {
    el.querySelector<HTMLElement>('[data-testid="salvar"]')!.click();
    await atualizar();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    http.verify();
  });

  it('abre preenchido, sem abas de tipo, parcelas nem duplicar', async () => {
    await abrir();
    expect(el.querySelector('#lancamento-titulo')!.textContent).toContain('Editar compra');
    expect(el.querySelector('[role="radiogroup"]')).toBeNull();
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('12,95');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Uber');
    expect(el.querySelector<HTMLInputElement>('#lancamento-data')!.value).toBe('2026-03-10');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-cartao')!.value).toBe('c-azul');
    expect(el.querySelector('#lancamento-parcelas')).toBeNull();
    expect(el.querySelector('[data-testid="duplicar"]')).toBeNull();
    expect(el.querySelector('[data-testid="excluir"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="salvar-outro"]')).toBeNull();
  });

  it('salva por PUT com a versão e o escopo "só esta"', async () => {
    await abrir();
    await digitar('#lancamento-descricao', 'Uber Centro');
    await salvar();
    const req = http.expectOne('/api/v1/card-purchases/p1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      description: 'Uber Centro',
      amount: '12,95',
      nature: 'debit',
      date: '2026-03-10',
      creditCardId: 'c-azul',
      notes: 'Corrida',
      scope: 'this',
      version: 1,
      tagIds: [],
    });
    req.flush(compra());
    expect(fechar).toHaveBeenCalled();
  });

  it('crédito abre com valor negativo e volta como natureza crédito', async () => {
    await abrir(compra({ nature: 'credit', amountCents: 9990, description: 'Estorno Óculos' }));
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('-99,90');
    await salvar();
    const req = http.expectOne('/api/v1/card-purchases/p1');
    expect(req.request.body).toMatchObject({ amount: '99,90', nature: 'credit' });
    req.flush(compra());
  });

  it('409 de fatura paga pede confirmação e repete com confirmPaidInvoice', async () => {
    await abrir();
    const dialogo = vi
      .spyOn(TestBed.inject(Dialogo), 'open')
      .mockReturnValue({ afterClosed: () => of('') } as never);
    await salvar();
    http.expectOne('/api/v1/card-purchases/p1').flush(
      {
        code: 'card_purchase.invoice_paid_confirmation_required',
        detail: 'Esta fatura já foi paga e voltará a ter valor em aberto.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await atualizar();
    expect(dialogo).toHaveBeenCalled();
    const repetido = http.expectOne('/api/v1/card-purchases/p1');
    expect(repetido.request.body.confirmPaidInvoice).toBe(true);
    repetido.flush(compra());
  });

  it('409 de versão recarrega a compra e avisa', async () => {
    await abrir();
    await salvar();
    http
      .expectOne('/api/v1/card-purchases/p1')
      .flush(
        { code: 'concurrency_conflict', detail: 'x' },
        { status: 409, statusText: 'Conflict' },
      );
    http
      .expectOne('/api/v1/card-purchases/p1')
      .flush(compra({ description: 'Uber (outro)', version: 2 }));
    await atualizar();
    expect(el.querySelector('[data-testid="erro-lancamento"]')!.textContent).toContain(
      'alterado em outro aparelho',
    );
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Uber (outro)');
  });

  describe('parcelas (US3.4, US3.5)', () => {
    const parcela = () =>
      compra({
        id: 'p2',
        description: 'Casa China',
        displayDescription: 'Casa China 2/3',
        installmentPlanId: 'plano-1',
        installmentNumber: 2,
        installmentTotal: 3,
      });
    const escolher = (valor: string | null) =>
      vi.spyOn(TestBed.inject(Dialogo), 'open').mockReturnValue({
        afterClosed: () => of(valor),
      } as never);

    it('editar pergunta "só esta ou todas" e envia o escopo escolhido', async () => {
      await abrir(parcela());
      const dialogo = escolher('all');
      await digitar('#lancamento-descricao', 'Casa China Nova');
      await salvar();
      expect(dialogo.mock.calls[0][1]).toMatchObject({
        data: { titulo: 'Alterar só esta parcela ou todas?' },
      });
      const req = http.expectOne('/api/v1/card-purchases/p2');
      expect(req.request.body).toMatchObject({ description: 'Casa China Nova', scope: 'all' });
      req.flush(parcela());
      expect(fechar).toHaveBeenCalled();
    });

    it('cancelar a pergunta não salva', async () => {
      await abrir(parcela());
      escolher(null);
      await salvar();
      http.expectNone('/api/v1/card-purchases/p2');
      expect(fechar).not.toHaveBeenCalled();
    });

    it('formulário inválido não chega a perguntar o escopo', async () => {
      await abrir(parcela());
      const dialogo = escolher('this');
      await digitar('#lancamento-descricao', '');
      await salvar();
      expect(dialogo).not.toHaveBeenCalled();
    });

    it('excluir oferece "só esta / esta e as seguintes / todas" e o Desfazer', async () => {
      await abrir(parcela());
      const dialogo = escolher('this_and_following');
      el.querySelector<HTMLElement>('[data-testid="excluir"]')!.click();
      await atualizar();
      expect(dialogo.mock.calls[0][1]).toMatchObject({
        data: {
          opcoes: [{ valor: 'this' }, { valor: 'this_and_following' }, { valor: 'all' }],
        },
      });
      const req = http.expectOne('/api/v1/card-purchases/p2?scope=this_and_following');
      expect(req.request.method).toBe('DELETE');
      req.flush({ deletionBatchId: 'lote-9' });
      expect(fechar).toHaveBeenCalled();
      expect(TestBed.inject(Avisos).open).toHaveBeenCalledWith(
        'Excluído',
        'Desfazer',
        expect.objectContaining({ duration: 8000 }),
      );
    });
  });

  it('compra à vista exclui direto, sem perguntar o escopo', async () => {
    await abrir();
    const dialogo = vi.spyOn(TestBed.inject(Dialogo), 'open');
    el.querySelector<HTMLElement>('[data-testid="excluir"]')!.click();
    await atualizar();
    expect(dialogo).not.toHaveBeenCalled();
    http.expectOne('/api/v1/card-purchases/p1?scope=this').flush({ deletionBatchId: 'lote-1' });
    expect(fechar).toHaveBeenCalled();
  });
});
