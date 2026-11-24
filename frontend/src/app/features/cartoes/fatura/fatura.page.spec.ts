import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { carteira } from '../../carteiras/testing';
import { bruna, ifood } from '../../../shared/tag-picker/testing';
import { LancamentoLauncher } from '../../lancamento/lancamento.launcher';
import { cartao } from '../testing';
import { FaturaPage } from './fatura.page';
import { compra, faturaDetalhada, pagamento } from './testing';

const BASE = '/api/v1/credit-cards/c-azul/invoices/2026-03';

describe('FaturaPage (US5.2–US5.5, US6.2, US7)', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<FaturaPage>;
  let el: HTMLElement;
  let dialog: ReturnType<typeof vi.spyOn>;
  let launcher: { abrirCompra: ReturnType<typeof vi.fn> };
  let aviso: ReturnType<typeof vi.spyOn>;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function abrir(fatura = faturaDetalhada(), mes = '2026-03') {
    launcher = { abrirCompra: vi.fn().mockReturnValue({ afterClosed: () => of(undefined) }) };
    TestBed.configureTestingModule({
      imports: [FaturaPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: 'c-azul', mes })) },
        },
        { provide: LancamentoLauncher, useValue: launcher },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    dialog = vi.spyOn(TestBed.inject(MatDialog), 'open');
    aviso = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue({
      onAction: () => of(),
    } as never);
    fixture = TestBed.createComponent(FaturaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http
      .expectOne('/api/v1/wallets?archived=all')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde' })]);
    if (mes === '2026-03') {
      http.expectOne('/api/v1/credit-cards/c-azul').flush(cartao());
      http.expectOne(BASE).flush(fatura);
    }
    await atualizar();
  }

  const texto = (id: string) => el.querySelector(`[data-testid="${id}"]`)?.textContent?.trim();
  const clicar = async (id: string, indice = 0) => {
    el.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`)[indice].click();
    await atualizar();
  };
  const respondeDialogo = (valor: unknown) =>
    dialog.mockReturnValue({ afterClosed: () => of(valor) } as never);

  afterEach(() => http.verify());

  it('FR-011: bloco "Por pessoa" com subtotais por Tag de acerto e "Sem tag de acerto" somando o Total', async () => {
    launcher = { abrirCompra: vi.fn() };
    TestBed.configureTestingModule({
      imports: [FaturaPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: 'c-azul', mes: '2026-03' })) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FaturaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    http.expectOne('/api/v1/credit-cards/c-azul').flush(cartao());
    http.expectOne(BASE).flush(
      faturaDetalhada({
        settlementSubtotals: [
          { tagId: 't-bruna', cents: 15000 },
          { tagId: null, cents: 5000 },
        ],
      }),
    );
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush([bruna, ifood]));
    await atualizar();
    const linhas = Array.from(el.querySelectorAll('[data-testid="subtotal-pessoa"]')).map((l) =>
      l.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(linhas).toEqual(['Bruna R$ 150,00', 'Sem tag de acerto R$ 50,00']);
  });

  it('sem subtotais o bloco "Por pessoa" não aparece', async () => {
    await abrir();
    expect(el.querySelector('[data-testid="por-pessoa"]')).toBeNull();
  });

  it('mostra datas, situação, totais e "Conferido" da fatura do exemplo (US5.2, US6.2, FR-027)', async () => {
    await abrir(
      faturaDetalhada({
        reconciledCents: 6000,
        purchases: [
          compra({ id: 'p1', description: 'Uber', displayDescription: 'Uber', reconciled: true }),
          compra({
            id: 'p2',
            displayDescription: 'Estorno Óculos',
            amountCents: 9990,
            nature: 'credit',
          }),
        ],
      }),
    );
    expect(texto('titulo-fatura')).toBe('Fatura de março');
    expect(texto('datas-fatura')).toBe('Fecha em 18/03 · vence em 25/03');
    expect(texto('situacao')).toBe('Fechada');
    expect(texto('total')).toContain('R$ 200,00');
    expect(texto('pago')).toContain('R$ 0,00');
    expect(texto('restante')).toContain('R$ 200,00');
    expect(texto('conferido')).toBe('Conferido R$ 60,00 de R$ 200,00');
    const valores = Array.from(el.querySelectorAll('[data-testid="valor"]')).map((v) =>
      v.textContent?.trim().replace(/\s/g, ' '),
    );
    expect(valores).toEqual(['R$ 12,95', '-R$ 99,90']);
    expect(el.querySelector<HTMLInputElement>('[data-testid="conferida"]')!.checked).toBe(true);
  });

  it('navega para a fatura anterior e a seguinte pelos meses vizinhos', async () => {
    await abrir();
    expect(el.querySelector('[data-testid="fatura-anterior"]')!.getAttribute('href')).toBe(
      '/cartoes/c-azul/faturas/2026-02',
    );
    expect(el.querySelector('[data-testid="fatura-seguinte"]')!.getAttribute('href')).toBe(
      '/cartoes/c-azul/faturas/2026-04',
    );
  });

  it('mês inválido na URL mostra erro sem chamar a fatura', async () => {
    await abrir(faturaDetalhada(), 'abc');
    expect(texto('erro-fatura')).toContain('Fatura não encontrada');
  });

  it('situações: Vencida, e crédito quando o total é negativo', async () => {
    await abrir(faturaDetalhada({ status: 'overdue', remainingCents: 5000 }));
    expect(texto('situacao')).toBe('Vencida');
    TestBed.resetTestingModule();
    await abrir(
      faturaDetalhada({ status: 'no_amount_due', totalCents: -3000, remainingCents: -3000 }),
    );
    expect(texto('situacao')).toBe('Sem valor a pagar — crédito de R$ 30,00');
  });

  it('marcar a caixinha confere a compra e recarrega (US7.1)', async () => {
    await abrir(faturaDetalhada({ purchases: [compra()] }));
    await clicar('conferida');
    const req = http.expectOne('/api/v1/card-purchases/p1/reconciled');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ reconciled: true });
    req.flush(compra({ reconciled: true }));
    http
      .expectOne(BASE)
      .flush(faturaDetalhada({ reconciledCents: 1295, purchases: [compra({ reconciled: true })] }));
    await atualizar();
    expect(texto('conferido')).toBe('Conferido R$ 12,95 de R$ 200,00');
  });

  it('"Editar" abre o formulário com a compra e recarrega ao fechar', async () => {
    const p = compra();
    await abrir(faturaDetalhada({ purchases: [p] }));
    await clicar('editar-compra');
    expect(launcher.abrirCompra).toHaveBeenCalledWith(p);
    http.expectOne(BASE).flush(faturaDetalhada({ purchases: [p] }));
  });

  it('compra de saldo levado não tem "Editar"', async () => {
    await abrir(faturaDetalhada({ purchases: [compra({ origin: 'carry_over' })] }));
    expect(el.querySelector('[data-testid="editar-compra"]')).toBeNull();
  });

  it('move compra à vista direto; a parcela pergunta "só esta / esta e as seguintes" (US7.2, US7.3)', async () => {
    await abrir(
      faturaDetalhada({
        purchases: [
          compra({ id: 'a' }),
          compra({
            id: 'b',
            displayDescription: 'Casa China 2/4',
            installmentPlanId: 'plano',
            installmentNumber: 2,
            installmentTotal: 4,
          }),
        ],
      }),
    );
    await clicar('mover-seguinte', 0);
    const req = http.expectOne('/api/v1/card-purchases/a/move');
    expect(req.request.body).toEqual({ direction: 'next', scope: 'this' });
    req.flush([]);
    http.expectOne(BASE).flush(faturaDetalhada());
    await atualizar();
    expect(dialog).not.toHaveBeenCalled();
  });

  it('parcela: escolhe "esta e as seguintes" ao mover', async () => {
    await abrir(
      faturaDetalhada({
        purchases: [
          compra({
            id: 'b',
            installmentPlanId: 'plano',
            installmentNumber: 2,
            installmentTotal: 4,
          }),
        ],
      }),
    );
    respondeDialogo('this_and_following');
    await clicar('mover-seguinte');
    const req = http.expectOne('/api/v1/card-purchases/b/move');
    expect(req.request.body).toEqual({ direction: 'next', scope: 'this_and_following' });
    req.flush([]);
    http.expectOne(BASE).flush(faturaDetalhada());
  });

  it('cancelar o escopo não chama a API', async () => {
    await abrir(
      faturaDetalhada({
        purchases: [
          compra({
            id: 'b',
            installmentPlanId: 'plano',
            installmentNumber: 2,
            installmentTotal: 4,
          }),
        ],
      }),
    );
    respondeDialogo(null);
    await clicar('excluir-compra');
    expect(dialog).toHaveBeenCalled();
  });

  it('exclui compra à vista com Desfazer (RT-09)', async () => {
    await abrir(faturaDetalhada({ purchases: [compra()] }));
    await clicar('excluir-compra');
    const req = http.expectOne('/api/v1/card-purchases/p1?scope=this');
    req.flush({ deletionBatchId: 'lote-1' });
    http.expectOne(BASE).flush(faturaDetalhada());
    await atualizar();
    expect(aviso).toHaveBeenCalledWith(
      'Excluído',
      'Desfazer',
      expect.objectContaining({ duration: 8000 }),
    );
  });

  it('exclui parcela com o escopo escolhido', async () => {
    await abrir(
      faturaDetalhada({
        purchases: [
          compra({
            id: 'b',
            installmentPlanId: 'plano',
            installmentNumber: 2,
            installmentTotal: 4,
          }),
        ],
      }),
    );
    respondeDialogo('all');
    await clicar('excluir-compra');
    http.expectOne('/api/v1/card-purchases/b?scope=all').flush({ deletionBatchId: 'lote-2' });
    http.expectOne(BASE).flush(faturaDetalhada());
  });

  it('pagamentos: lista, edita (diálogo) e exclui com Desfazer', async () => {
    const pg = pagamento();
    await abrir(
      faturaDetalhada({ status: 'closed', paidCents: 15000, remainingCents: 5000, payments: [pg] }),
    );
    expect(el.querySelector('[data-testid="pagamento"]')!.textContent).toContain('20/03');
    expect(el.querySelector('[data-testid="pagamento"]')!.textContent).toContain('Azul');
    expect(texto('valor-pagamento')).toContain('R$ 150,00');

    respondeDialogo(true);
    await clicar('editar-pagamento');
    expect(dialog.mock.calls[0][1].data.pagamento).toEqual(pg);
    http.expectOne(BASE).flush(faturaDetalhada({ payments: [pg] }));
    await atualizar();

    await clicar('excluir-pagamento');
    http.expectOne('/api/v1/invoice-payments/pg1').flush({ deletionBatchId: 'lote-3' });
    http.expectOne(BASE).flush(faturaDetalhada());
  });

  it('"Pagar" abre o diálogo com o restante e a carteira de pagamento do cartão (US5.1)', async () => {
    await abrir();
    respondeDialogo(true);
    await clicar('pagar');
    const data = dialog.mock.calls[0][1].data;
    expect(data).toMatchObject({
      cardId: 'c-azul',
      month: '2026-03',
      remainingCents: 20000,
      carteiraPadraoId: 'w-azul',
    });
    http.expectOne(BASE).flush(faturaDetalhada({ status: 'paid', remainingCents: 0 }));
    await atualizar();
    expect(el.querySelector('[data-testid="pagar"]')).toBeNull();
  });

  it('"Levar restante" só aparece quando permitido e cria o par (US5.4)', async () => {
    await abrir(faturaDetalhada({ status: 'overdue', remainingCents: 5000 }));
    await clicar('levar-restante');
    http
      .expectOne(`${BASE}/carry-over`)
      .flush(faturaDetalhada({ status: 'paid', remainingCents: 0, canCarryOver: false }));
    http
      .expectOne(BASE)
      .flush(faturaDetalhada({ status: 'paid', remainingCents: 0, canCarryOver: false }));
    await atualizar();
    expect(el.querySelector('[data-testid="levar-restante"]')).toBeNull();
  });

  it('erro da API na ação aparece no aviso da tela', async () => {
    await abrir(faturaDetalhada({ purchases: [compra()] }));
    await clicar('mover-anterior');
    http
      .expectOne('/api/v1/card-purchases/p1/move')
      .flush({ code: 'x', detail: 'Não foi possível mover.' }, { status: 422, statusText: 'x' });
    await atualizar();
    expect(texto('aviso-fatura')).toBe('Não foi possível mover.');
  });

  describe('filtro por tag (004 FR-004)', () => {
    it('refaz a consulta com tagIds e mostra o total das compras filtradas', async () => {
      await abrir(faturaDetalhada({ purchases: [compra({ id: 'p1' }), compra({ id: 'p2' })] }));
      expect(el.querySelector('[data-testid="total-filtrado"]')).toBeNull();

      await clicar('filtro-tags-abrir');
      http.expectOne('/api/v1/tags?archived=all').flush([bruna, ifood]);
      await atualizar();
      await clicar('filtro-tag', 0); // "Bruna" (mais usada)

      http.expectOne(`${BASE}?tagIds=t-bruna`).flush(
        faturaDetalhada({
          purchases: [compra({ id: 'p1', tagIds: ['t-bruna'] })],
          filteredTotalCents: 1295,
        }),
      );
      await atualizar();
      expect(el.querySelectorAll('[data-testid="compra"]')).toHaveLength(1);
      expect(texto('total-filtrado')).toContain('R$ 12,95');
      // Total da fatura segue sendo o da fatura inteira
      expect(texto('total')).toContain('R$ 200,00');
    });

    it('sem compras com a tag mostra a mensagem do filtro', async () => {
      await abrir();
      await clicar('filtro-tags-abrir');
      http.expectOne('/api/v1/tags?archived=all').flush([bruna]);
      await atualizar();
      await clicar('filtro-tag');
      http
        .expectOne(`${BASE}?tagIds=t-bruna`)
        .flush(faturaDetalhada({ purchases: [], filteredTotalCents: 0 }));
      await atualizar();
      expect(texto('sem-compras')).toBe('Nenhuma compra com estas tags nesta fatura.');
    });
  });
});
