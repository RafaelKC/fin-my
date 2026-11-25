import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { carteira } from '../../carteiras/testing';
import { PagamentoDialog, PagamentoDialogData } from './pagamento.dialog';
import { pagamento } from './testing';
import { DADOS_DIALOGO, DialogoRef } from '@/shared/ui/dialogo';

describe('PagamentoDialog (FR-017, FR-018)', () => {
  let fixture: ComponentFixture<PagamentoDialog>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;

  async function abrir(extra: Partial<PagamentoDialogData> = {}) {
    fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [PagamentoDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DialogoRef, useValue: { close: fechar } },
        {
          provide: DADOS_DIALOGO,
          useValue: {
            cardId: 'c1',
            month: '2026-03',
            carteiras: [carteira(), carteira({ id: 'w-verde', name: 'Verde' })],
            remainingCents: 20000,
            carteiraPadraoId: 'w-azul',
            ...extra,
          } satisfies PagamentoDialogData,
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PagamentoDialog);
    el = fixture.nativeElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  const valorCampo = () => el.querySelector<HTMLInputElement>('#amount-field')!;
  const confirmar = async () => {
    el.querySelector<HTMLElement>('[data-testid="confirmar-pagamento"]')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  afterEach(() => http.verify());

  it('abre com o restante, hoje e a carteira de pagamento do cartão', async () => {
    await abrir();
    expect(valorCampo().value).toBe('200,00');
    expect(el.querySelector<HTMLSelectElement>('#pagamento-carteira')!.value).toBe('w-azul');
    await confirmar();
    const req = http.expectOne('/api/v1/credit-cards/c1/invoices/2026-03/payments');
    expect(req.request.body.amount).toBe('200,00');
    expect(req.request.body.walletId).toBe('w-azul');
    expect(req.request.headers.get('Idempotency-Key')).toBeTruthy();
    req.flush({});
    expect(fechar).toHaveBeenCalledWith(true);
  });

  it('valor acima do restante mostra a mensagem da API e não fecha (US5.5)', async () => {
    await abrir({ remainingCents: 5000 });
    valorCampo().value = '60';
    valorCampo().dispatchEvent(new Event('input'));
    await confirmar();
    http
      .expectOne('/api/v1/credit-cards/c1/invoices/2026-03/payments')
      .flush(
        { code: 'invoice.payment_exceeds_remaining', detail: 'O valor em aberto é R$ 50,00.' },
        { status: 422, statusText: 'Unprocessable' },
      );
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-pagamento"]')!.textContent).toContain(
      'O valor em aberto é R$ 50,00.',
    );
    expect(fechar).not.toHaveBeenCalled();
  });

  it('edita um pagamento por PUT com a versão', async () => {
    await abrir({ pagamento: pagamento({ version: 4 }) });
    expect(valorCampo().value).toBe('150,00');
    await confirmar();
    const req = http.expectOne('/api/v1/invoice-payments/pg1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      amount: '150,00',
      date: '2026-03-20',
      walletId: 'w-azul',
      version: 4,
    });
    req.flush({});
    expect(fechar).toHaveBeenCalledWith(true);
  });

  it('valor inválido não chama a API', async () => {
    await abrir();
    valorCampo().value = 'abc';
    valorCampo().dispatchEvent(new Event('input'));
    await confirmar();
    expect(el.querySelector('[data-testid="erro-pagamento"]')!.textContent).toContain(
      'valor válido',
    );
  });
});
