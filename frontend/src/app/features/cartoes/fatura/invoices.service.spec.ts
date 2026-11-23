import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { InvoicesService } from './invoices.service';

describe('InvoicesService', () => {
  let service: InvoicesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(InvoicesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('busca a fatura do cartão no mês', () => {
    service.get('c1', '2026-03').subscribe();
    const req = http.expectOne('/api/v1/credit-cards/c1/invoices/2026-03');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('paga com Idempotency-Key e o corpo informado', () => {
    service.pay('c1', '2026-03', { amount: '150,00', date: '2026-03-20' }, 'chave-1').subscribe();
    const req = http.expectOne('/api/v1/credit-cards/c1/invoices/2026-03/payments');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(req.request.body).toEqual({ amount: '150,00', date: '2026-03-20' });
    req.flush({});
  });

  it('leva o restante, exclui todos os pagamentos e edita/exclui um pagamento', () => {
    service.carryOver('c1', '2026-03').subscribe();
    expect(
      http.expectOne('/api/v1/credit-cards/c1/invoices/2026-03/carry-over').request.method,
    ).toBe('POST');
    service.deletePayments('c1', '2026-03').subscribe();
    expect(http.expectOne('/api/v1/credit-cards/c1/invoices/2026-03/payments').request.method).toBe(
      'DELETE',
    );
    service
      .updatePayment('pg1', { amount: '10,00', date: '2026-03-21', walletId: 'w1', version: 2 })
      .subscribe();
    const put = http.expectOne('/api/v1/invoice-payments/pg1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.version).toBe(2);
    service.deletePayment('pg1').subscribe();
    expect(http.match('/api/v1/invoice-payments/pg1').pop()!.request.method).toBe('DELETE');
  });
});
