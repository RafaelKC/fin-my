import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { today } from '../../core/money';
import { carteira } from '../carteiras/testing';
import { cartao } from '../cartoes/testing';
import { LancamentoSheet } from './lancamento.sheet';

const azul = cartao();
const verde = cartao({ id: 'c-verde', name: 'Verde', closingDay: 1, dueDay: 10 });
const resultado = (warnings: string[] = []) => ({ purchases: [], warnings });

describe('LancamentoSheet — aba Cartão (003 US2–US4)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;
  let aviso: ReturnType<typeof vi.spyOn>;

  async function abrir(cartoes = [verde, azul]) {
    fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: fechar } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    aviso = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=false').flush([carteira()]);
    http.expectOne('/api/v1/credit-cards?archived=false').flush(cartoes);
    await atualizar();
  }
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function digitar(seletor: string, valor: string) {
    const campo = el.querySelector<HTMLInputElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  }
  async function escolher(seletor: string, valor: string) {
    const campo = el.querySelector<HTMLSelectElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('change'));
    await atualizar();
  }
  const clicar = async (seletor: string) => {
    el.querySelector<HTMLElement>(seletor)!.click();
    await atualizar();
  };
  const radio = (nome: string) =>
    Array.from(el.querySelectorAll<HTMLElement>('[role="radio"]')).find(
      (b) => b.textContent?.trim() === nome,
    )!;
  const tem = (seletor: string) => el.querySelector(seletor) !== null;
  const texto = (seletor: string) => el.querySelector(seletor)?.textContent?.trim();
  const cartaoEscolhido = () => el.querySelector<HTMLSelectElement>('#lancamento-cartao')!.value;
  const criacao = () => http.expectOne('/api/v1/card-purchases');
  const pausa = (ms: number) => new Promise((r) => setTimeout(r, ms));

  async function preencher(valor: string, descricao: string, data: string) {
    await digitar('#amount-field', valor);
    await digitar('#lancamento-descricao', descricao);
    await digitar('#lancamento-data', data);
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    try {
      http.verify();
    } finally {
      localStorage.clear();
    }
  });

  it('primeiro uso abre em Cartão, com o primeiro cartão ativo em ordem alfabética (FR-010)', async () => {
    await abrir();
    expect(radio('Cartão').getAttribute('aria-checked')).toBe('true');
    expect(cartaoEscolhido()).toBe('c-azul');
    expect(tem('#lancamento-carteira')).toBe(false);
    expect(tem('#lancamento-situacao')).toBe(false);
    expect(tem('#lancamento-parcelas')).toBe(true);
  });

  it('usa o último cartão usado no aparelho; cartão que sumiu cai no primeiro (R9)', async () => {
    localStorage.setItem(
      'finmy.prefs.v1',
      JSON.stringify({ lastKind: 'card', lastCardId: 'c-verde' }),
    );
    await abrir();
    expect(cartaoEscolhido()).toBe('c-verde');
    fixture.destroy();
    http.verify();
    TestBed.resetTestingModule();

    localStorage.setItem(
      'finmy.prefs.v1',
      JSON.stringify({ lastKind: 'card', lastCardId: 'c-arquivado' }),
    );
    await abrir();
    expect(cartaoEscolhido()).toBe('c-azul');
  });

  it('US2.1: "Uber" 12,95 em 17/03 mostra a fatura e salva à vista com Idempotency-Key', async () => {
    await abrir();
    await preencher('12,95', 'Uber', '2026-03-17');
    expect(texto('[data-testid="previa-fatura"]')).toBe('Fatura de março — vence 25/03');

    await clicar('[data-testid="salvar"]');
    const req = criacao();
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(req.request.body).toEqual({
      description: 'Uber',
      amount: '12,95',
      date: '2026-03-17',
      creditCardId: 'c-azul',
    });
    req.flush(resultado());
    await atualizar();

    expect(fechar).toHaveBeenCalledWith(undefined);
    expect(aviso.mock.calls[0][0]).toBe('Lançamento salvo.');
    expect(JSON.parse(localStorage.getItem('finmy.prefs.v1')!)).toMatchObject({
      lastKind: 'card',
      lastCardId: 'c-azul',
    });
  });

  it('a fatura muda com o cartão e com a data (compra no fechamento vai para a seguinte)', async () => {
    await abrir();
    await digitar('#lancamento-data', '2026-03-18');
    expect(texto('[data-testid="previa-fatura"]')).toBe('Fatura de abril — vence 25/04');
    await escolher('#lancamento-cartao', 'c-verde');
    await digitar('#lancamento-data', '2026-01-31');
    expect(texto('[data-testid="previa-fatura"]')).toBe('Fatura de fevereiro — vence 10/02');
    await digitar('#lancamento-data', '2026-12-31');
    expect(texto('[data-testid="previa-fatura"]')).toBe(
      'Fatura de janeiro de 2027 — vence 10/01/2027',
    );
  });

  it('US2.2: valor negativo avisa estorno/crédito e envia o valor como digitado', async () => {
    await abrir();
    await preencher('-99,90', 'Estorno Óculos', '2026-03-17');
    expect(tem('[data-testid="aviso-credito"]')).toBe(true);

    await clicar('[data-testid="salvar"]');
    const req = criacao();
    expect(req.request.body.amount).toBe('-99,90');
    req.flush(resultado(['recorded_as_credit']));
    await atualizar();
    expect(aviso.mock.calls[0][0]).toBe('Lançado como estorno/crédito.');
    expect(fechar).toHaveBeenCalled();
  });

  it('valor zero não salva; estorno parcelado é recusado antes de chamar a API', async () => {
    await abrir();
    await preencher('0', 'Nada', '2026-03-17');
    await clicar('[data-testid="salvar"]');
    http.expectNone('/api/v1/card-purchases');

    await digitar('#amount-field', '-50');
    await escolher('#lancamento-parcelas', '3');
    await clicar('[data-testid="salvar"]');
    http.expectNone('/api/v1/card-purchases');
    expect(texto('[data-testid="erro-parcelas"]')).toBe('Estorno ou crédito é sempre à vista.');
  });

  it('US3.3 e US3.1: 100,00 em 3x mostra "3x de R$ 33,34 (1ª) e R$ 33,33" e envia o modo valor total', async () => {
    await abrir();
    await preencher('100', 'Casa China', '2026-01-28');
    await escolher('#lancamento-parcelas', '3');
    expect(texto('[data-testid="previa-fatura"]')).toBe(
      '3x de R$ 33,34 (1ª) e R$ 33,33 — primeira fatura: fevereiro',
    );

    await clicar('[data-testid="salvar"]');
    const req = criacao();
    expect(req.request.body.installments).toEqual({ total: 3, mode: 'total_amount' });
    req.flush(resultado());
  });

  it('US3.2: modo "valor da parcela" mostra o total e envia installment_amount', async () => {
    await abrir();
    await preencher('102,79', 'Shoppe Casa', '2026-03-17');
    await escolher('#lancamento-parcelas', '8');
    await clicar(
      '[role="radiogroup"][aria-labelledby="lancamento-modo"] [role="radio"]:nth-child(2)',
    );
    expect(texto('[data-testid="previa-fatura"]')).toBe(
      '8x de R$ 102,79 (total R$ 822,32) — primeira fatura: março',
    );

    await clicar('[data-testid="salvar"]');
    const req = criacao();
    expect(req.request.body.installments).toEqual({ total: 8, mode: 'installment_amount' });
    req.flush(resultado());
  });

  it('US4.1: parcelamento em andamento 5 de 8 mostra as 4 restantes e envia in_progress', async () => {
    await abrir();
    await preencher('102,74', 'Shoppe Casa', '2026-06-20');
    await clicar('[data-testid="em-andamento"]');
    await escolher('#lancamento-parcelas', '8');
    await digitar('#lancamento-proxima', '5');
    await digitar('#lancamento-fatura-parcela', '2026-11');
    expect(texto('[data-testid="previa-fatura"]')).toBe(
      '4 parcelas restantes (5/8 a 8/8) de R$ 102,74 — primeira fatura: novembro',
    );

    await clicar('[data-testid="salvar"]');
    const req = criacao();
    expect(req.request.body).toEqual({
      description: 'Shoppe Casa',
      amount: '102,74',
      date: '2026-06-20',
      creditCardId: 'c-azul',
      installments: {
        total: 8,
        mode: 'in_progress',
        nextNumber: 5,
        firstInvoiceMonth: '2026-11',
      },
    });
    req.flush(resultado());
  });

  it('US4.2: próxima parcela maior que o total impede salvar', async () => {
    await abrir();
    await preencher('102,74', 'Shoppe Casa', '2026-06-20');
    await clicar('[data-testid="em-andamento"]');
    await escolher('#lancamento-parcelas', '8');
    await digitar('#lancamento-proxima', '9');
    await clicar('[data-testid="salvar"]');
    http.expectNone('/api/v1/card-purchases');
    expect(texto('[data-testid="erro-parcelas"]')).toBe(
      'A próxima parcela deve estar entre 1 e 8.',
    );
  });

  it('fatura da parcela p começa na fatura atual do cartão (FR-014)', async () => {
    await abrir();
    await clicar('[data-testid="em-andamento"]');
    const esperado = today().slice(0, 7);
    const valor = el.querySelector<HTMLInputElement>('#lancamento-fatura-parcela')!.value;
    // a fatura atual é a do mês da data ou a seguinte
    expect([esperado, nextMonth(esperado)]).toContain(valor);
    expect(el.querySelector('label[for="lancamento-data"]')?.textContent).toBe('Data da compra');
  });

  it('US5.7: 409 de fatura paga pede confirmação e repete com confirmPaidInvoice e outra chave', async () => {
    await abrir();
    const dialogo = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of('') } as never);
    await preencher('50', 'Lanche', '2026-03-10');
    await clicar('[data-testid="salvar"]');
    const primeira = criacao();
    const chave = primeira.request.headers.get('Idempotency-Key');
    primeira.flush(
      {
        status: 409,
        code: 'card_purchase.invoice_paid_confirmation_required',
        detail: 'Esta fatura já foi paga e voltará a ter valor em aberto',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await atualizar();

    expect(dialogo.mock.calls[0][1]?.data).toMatchObject({
      mensagem: 'Esta fatura já foi paga e voltará a ter valor em aberto',
      confirmar: 'Lançar mesmo assim',
    });
    const segunda = criacao();
    expect(segunda.request.body.confirmPaidInvoice).toBe(true);
    expect(segunda.request.headers.get('Idempotency-Key')).not.toBe(chave);
    segunda.flush(resultado());
    await atualizar();
    expect(fechar).toHaveBeenCalled();
  });

  it('cancelar a confirmação de fatura paga não lança nada e mantém o formulário', async () => {
    await abrir();
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of(null),
    } as never);
    await preencher('50', 'Lanche', '2026-03-10');
    await clicar('[data-testid="salvar"]');
    criacao().flush(
      { status: 409, code: 'card_purchase.invoice_paid_confirmation_required', detail: 'Paga' },
      { status: 409, statusText: 'Conflict' },
    );
    await atualizar();
    http.expectNone('/api/v1/card-purchases');
    expect(fechar).not.toHaveBeenCalled();
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Lanche');
  });

  it('sugestões consultam kind=card_purchase e aplicam o cartão, sem trocar o escolhido à mão (FR-011)', async () => {
    await abrir();
    await digitar('#lancamento-descricao', 'Ube');
    await pausa(200);
    http
      .expectOne('/api/v1/entries/suggestions?q=Ube&kind=card_purchase')
      .flush([{ description: 'Uber', uses: 4, creditCardId: 'c-verde' }]);
    await atualizar();
    await clicar('[data-testid="sugestao"]');
    expect(cartaoEscolhido()).toBe('c-verde');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Uber');

    await escolher('#lancamento-cartao', 'c-azul');
    await digitar('#lancamento-descricao', 'Ub');
    await pausa(200);
    http
      .expectOne('/api/v1/entries/suggestions?q=Ub&kind=card_purchase')
      .flush([{ description: 'Uber', uses: 4, creditCardId: 'c-verde' }]);
    await atualizar();
    await clicar('[data-testid="sugestao"]');
    expect(cartaoEscolhido()).toBe('c-azul');
  });

  it('sem cartões: avisa e leva ao cadastro', async () => {
    await abrir([]);
    expect(tem('[data-testid="sem-cartoes"]')).toBe(true);
    const ir = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await clicar('[data-testid="novo-cartao"]');
    expect(fechar).toHaveBeenCalled();
    expect(ir).toHaveBeenCalledWith('/cartoes/novo');
  });

  it('"Salvar e lançar outro" mantém tipo, data e cartão e volta para à vista', async () => {
    await abrir();
    await preencher('100', 'Casa China', '2026-01-28');
    await escolher('#lancamento-cartao', 'c-verde');
    await escolher('#lancamento-parcelas', '3');
    await clicar('[data-testid="salvar-outro"]');
    const req = criacao();
    expect(req.request.body.creditCardId).toBe('c-verde');
    req.flush(resultado());
    await atualizar();

    expect(fechar).not.toHaveBeenCalled();
    expect(radio('Cartão').getAttribute('aria-checked')).toBe('true');
    expect(cartaoEscolhido()).toBe('c-verde');
    expect(el.querySelector<HTMLInputElement>('#lancamento-data')!.value).toBe('2026-01-28');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-parcelas')!.value).toBe('1');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('');
  });

  it('trocar para Saída esconde a aba Cartão e mostra carteira e situação', async () => {
    await abrir();
    radio('Saída').click();
    await atualizar();
    expect(tem('#lancamento-cartao')).toBe(false);
    expect(tem('#lancamento-carteira')).toBe(true);
    expect(tem('#lancamento-situacao')).toBe(true);
  });

  it('erro da API aparece no formulário, que continua aberto', async () => {
    await abrir();
    await preencher('50', 'Lanche', '2026-03-10');
    await clicar('[data-testid="salvar"]');
    criacao().flush(
      { status: 422, code: 'credit_card.archived', detail: 'Este cartão está arquivado.' },
      { status: 422, statusText: 'Unprocessable' },
    );
    await atualizar();
    expect(texto('[data-testid="erro-lancamento"]')).toBe('Este cartão está arquivado.');
    expect(fechar).not.toHaveBeenCalled();
  });
});

function nextMonth(m: string): string {
  const [ano, mes] = m.split('-').map(Number);
  return mes === 12 ? `${ano + 1}-01` : `${ano}-${String(mes + 1).padStart(2, '0')}`;
}
