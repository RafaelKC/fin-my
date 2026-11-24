import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlanningService } from './planning.service';

describe('PlanningService', () => {
  let http: HttpTestingController;
  let service: PlanningService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(PlanningService);
  });
  afterEach(() => http.verify());

  it('lê a grade do ano', () => {
    service.grid(2027).subscribe();
    const req = http.expectOne('/api/v1/years/2027/planning');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('edita uma célula com PUT', () => {
    const input = {
      kind: 'expense' as const,
      description: 'Natal',
      month: '2027-12',
      value: '1200',
    };
    service.setCell(2027, input).subscribe();
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(input);
    req.flush({});
  });

  it('preenche meses com POST e Idempotency-Key', () => {
    const input = {
      kind: 'invoice' as const,
      creditCardId: 'c-azul',
      amount: '800',
      months: ['2027-01', '2027-02'],
    };
    service.fill(2027, input, 'chave-1').subscribe();
    const req = http.expectOne('/api/v1/years/2027/planning/fill');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(req.request.body).toEqual(input);
    req.flush({});
  });

  it('lê os candidatos à cópia e copia com Idempotency-Key', () => {
    service.copyCandidates(2027).subscribe();
    const lista = http.expectOne('/api/v1/years/2027/planning/copy-candidates');
    expect(lista.request.method).toBe('GET');
    lista.flush({ rows: [], estimates: [] });

    const input = { rowKeys: ['expense:natal'], copyEstimates: true };
    service.copy(2027, input, 'chave-2').subscribe();
    const req = http.expectOne('/api/v1/years/2027/planning/copy');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-2');
    expect(req.request.body).toEqual(input);
    req.flush({ created: 2, skipped: 0 });
  });
});
