import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TransfersService } from './transfers.service';

describe('TransfersService', () => {
  it('cria por POST /transfers com Idempotency-Key e avisa as telas', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const service = TestBed.inject(TransfersService);
    const http = TestBed.inject(HttpTestingController);
    const avisos: string[] = [];
    service.criadas$.subscribe((t) => avisos.push(t.id));

    service
      .create({ fromWalletId: 'a', toWalletId: 'b', amount: '20', date: '2026-06-29' }, 'chave-1')
      .subscribe();
    const req = http.expectOne('/api/v1/transfers');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(req.request.body).toEqual({
      fromWalletId: 'a',
      toWalletId: 'b',
      amount: '20',
      date: '2026-06-29',
    });
    req.flush({ id: 't1' });
    expect(avisos).toEqual(['t1']);
  });
});
