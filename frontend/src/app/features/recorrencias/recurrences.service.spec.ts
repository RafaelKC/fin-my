import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RecurrenceInput } from './recurrence.models';
import { RecurrencesService } from './recurrences.service';
import { recorrencia } from './testing';

describe('RecurrencesService', () => {
  let service: RecurrencesService;
  let http: HttpTestingController;
  const input: RecurrenceInput = {
    kind: 'expense',
    description: 'Tim',
    amount: '44,99',
    dayOfMonth: 10,
    walletId: 'w-azul',
    creditCardId: null,
    tagIds: [],
    startMonth: '2026-03',
    endType: 'none',
    endMonth: null,
    occurrenceLimit: null,
    referenceMonthRule: 'same_month',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RecurrencesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista ativas por padrão e filtra por situação', () => {
    service.list().subscribe();
    http
      .expectOne('/api/v1/recurrences?status=active')
      .flush({ items: [], monthlyIncomeCents: 0, monthlyExpenseCents: 0 });
    service.list('ended').subscribe();
    http
      .expectOne('/api/v1/recurrences?status=ended')
      .flush({ items: [], monthlyIncomeCents: 0, monthlyExpenseCents: 0 });
  });

  it('cria com a chave de idempotência', () => {
    service.create(input, 'chave-1').subscribe();
    const req = http.expectOne('/api/v1/recurrences');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(req.request.body).toEqual(input);
    req.flush(recorrencia());
  });

  it('lê, altera a partir de um mês, encerra e exclui', () => {
    service.get('r-tim').subscribe();
    http.expectOne('/api/v1/recurrences/r-tim').flush(recorrencia());

    service.update('r-tim', { ...input, fromMonth: '2026-09', version: 2 }).subscribe();
    const put = http.expectOne('/api/v1/recurrences/r-tim');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({ fromMonth: '2026-09', version: 2 });
    put.flush({ ...recorrencia(), affectedCount: 4 });

    service.end('r-tim', '2026-10').subscribe();
    const end = http.expectOne('/api/v1/recurrences/r-tim/end');
    expect(end.request.body).toEqual({ fromMonth: '2026-10' });
    end.flush({ recurrence: recorrencia(), deletedCount: 15, deletionBatchId: 'b1' });

    service.delete('r-tim').subscribe();
    const del = http.expectOne('/api/v1/recurrences/r-tim');
    expect(del.request.method).toBe('DELETE');
    del.flush({ deletedCount: 3, unlinkedCount: 2 });
  });

  it('repete todo mês a partir de um lançamento, com a chave de idempotência', () => {
    service
      .fromItem({ type: 'entry', id: 'e1', endType: 'after_count', occurrenceLimit: 6 }, 'chave-2')
      .subscribe();
    const req = http.expectOne('/api/v1/recurrences/from-item');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-2');
    expect(req.request.body).toEqual({
      type: 'entry',
      id: 'e1',
      endType: 'after_count',
      occurrenceLimit: 6,
    });
    req.flush(recorrencia());
  });
});
