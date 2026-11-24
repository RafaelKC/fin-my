import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SettlementsService } from './settlements.service';
import { linha } from './testing';

describe('SettlementsService', () => {
  let http: HttpTestingController;
  let service: SettlementsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SettlementsService);
  });
  afterEach(() => http.verify());

  it('lista os acertos do mês', () => {
    let resposta: unknown;
    service.list('2026-03').subscribe((r) => (resposta = r));
    http.expectOne('/api/v1/settlements/2026-03').flush([linha()]);
    expect(resposta).toEqual([linha()]);
  });

  it('lista os lançamentos de uma tag no mês', () => {
    service.items('2026-03', 't-bruna').subscribe();
    http.expectOne('/api/v1/settlements/2026-03/tags/t-bruna/items').flush([]);
  });
});
