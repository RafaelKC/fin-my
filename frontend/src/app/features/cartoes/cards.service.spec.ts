import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CreditCardInput } from './card.models';
import { CardsService } from './cards.service';

describe('CardsService', () => {
  let service: CardsService;
  let http: HttpTestingController;
  const input: CreditCardInput = {
    name: 'Azul',
    closingDay: 18,
    dueDay: 25,
    paymentWalletId: 'w-azul',
    color: '#1E88E5',
    limit: '3000,00',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CardsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista ativos por padrão e arquivados quando pedido', () => {
    service.list().subscribe();
    http.expectOne('/api/v1/credit-cards?archived=false').flush([]);
    service.list('true').subscribe();
    http.expectOne('/api/v1/credit-cards?archived=true').flush([]);
  });

  it('obtém um cartão com as faturas', () => {
    service.get('c1').subscribe();
    expect(http.expectOne('/api/v1/credit-cards/c1').request.method).toBe('GET');
  });

  it('cria com POST e edita com PUT levando a versão', () => {
    service.create(input).subscribe();
    const post = http.expectOne('/api/v1/credit-cards');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(input);

    service.update('c1', input, 3).subscribe();
    const put = http.expectOne('/api/v1/credit-cards/c1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ ...input, version: 3 });
  });

  it('arquiva, desarquiva e exclui', () => {
    service.archive('c1').subscribe();
    expect(http.expectOne('/api/v1/credit-cards/c1/archive').request.method).toBe('POST');
    service.unarchive('c1').subscribe();
    expect(http.expectOne('/api/v1/credit-cards/c1/unarchive').request.method).toBe('POST');
    service.delete('c1').subscribe();
    expect(http.expectOne('/api/v1/credit-cards/c1').request.method).toBe('DELETE');
  });
});
