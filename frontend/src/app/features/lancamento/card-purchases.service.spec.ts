import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CardPurchaseResult } from './card-purchase.models';
import { CardPurchasesService } from './card-purchases.service';

describe('CardPurchasesService', () => {
  let service: CardPurchasesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CardPurchasesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('create envia o corpo com o Idempotency-Key e avisa em criadas$', () => {
    const avisadas: CardPurchaseResult[] = [];
    service.criadas$.subscribe((r) => avisadas.push(r));
    let resposta: CardPurchaseResult | undefined;
    service
      .create(
        { description: 'Uber', amount: '12,95', date: '2026-03-17', creditCardId: 'c-azul' },
        'chave-1',
      )
      .subscribe((r) => (resposta = r));

    const req = http.expectOne('/api/v1/card-purchases');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(req.request.body).toEqual({
      description: 'Uber',
      amount: '12,95',
      date: '2026-03-17',
      creditCardId: 'c-azul',
    });
    req.flush({ purchases: [], warnings: [] });
    expect(resposta).toEqual({ purchases: [], warnings: [] });
    expect(avisadas).toHaveLength(1);
  });
});
