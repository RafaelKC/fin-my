import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EntriesService } from './entries.service';
import { Entry } from './entry.models';

describe('EntriesService', () => {
  let service: EntriesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EntriesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('create envia o corpo com o Idempotency-Key e avisa em criados$', () => {
    const criado: Entry = {
      id: 'e1',
      kind: 'expense',
      description: 'Tim',
      amountCents: 4499,
      date: '2026-03-10',
      referenceMonth: '2026-03',
      status: 'settled',
      walletId: 'w-azul',
      createdAt: '2026-03-10T15:00:00Z',
      version: 1,
    };
    const avisados: Entry[] = [];
    service.criados$.subscribe((e) => avisados.push(e));

    let resposta: Entry | undefined;
    service
      .create(
        {
          kind: 'expense',
          description: 'Tim',
          amount: '44,99',
          date: '2026-03-10',
          walletId: 'w-azul',
        },
        'chave-1',
      )
      .subscribe((e) => (resposta = e));
    const req = http.expectOne('/api/v1/entries');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(req.request.body).toMatchObject({ amount: '44,99' });
    req.flush(criado);

    expect(resposta).toEqual(criado);
    expect(avisados).toEqual([criado]);
  });

  it('suggestions codifica o texto e informa o tipo', () => {
    let lista: unknown;
    service.suggestions('ti & é', 'income').subscribe((l) => (lista = l));
    const req = http.expectOne(
      (r) => r.url === `/api/v1/entries/suggestions?q=${encodeURIComponent('ti & é')}&kind=income`,
    );
    req.flush([{ description: 'Tim', uses: 2, walletId: null }]);
    expect(lista).toEqual([{ description: 'Tim', uses: 2, walletId: null }]);
  });
});

describe('EntriesService (situação e edição)', () => {
  it('PATCH da situação e PUT da edição emitem alterados$', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const s = TestBed.inject(EntriesService);
    const vistos: string[] = [];
    s.alterados$.subscribe((x) => vistos.push(x.id));

    s.setStatus('e1', 'settled').subscribe();
    const patch = http.expectOne('/api/v1/entries/e1/status');
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ status: 'settled' });
    patch.flush({ id: 'e1' });

    s.update('e1', {
      kind: 'expense',
      description: 'Tim',
      amount: '44,99',
      date: '2026-03-10',
      walletId: 'w',
      status: 'settled',
      version: 2,
    }).subscribe();
    const put = http.expectOne('/api/v1/entries/e1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.version).toBe(2);
    put.flush({ id: 'e1' });
    expect(vistos).toEqual(['e1', 'e1']);
  });
});

describe('EntriesService (duplicar e buscar)', () => {
  let service: EntriesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EntriesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('duplicate envia só a data com Idempotency-Key e avisa em criados$', () => {
    const avisados: Entry[] = [];
    service.criados$.subscribe((e) => avisados.push(e));
    service.duplicate('e1', '2026-04-10', 'chave-1').subscribe();
    const req = http.expectOne('/api/v1/entries/e1/duplicate');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ date: '2026-04-10' });
    expect(req.request.headers.get('Idempotency-Key')).toBe('chave-1');
    req.flush({ id: 'e2' });
    expect(avisados).toHaveLength(1);
  });

  it('search monta a consulta só com os filtros preenchidos', () => {
    service
      .search({
        q: ' uber ',
        kinds: ['expense', 'transfer'],
        walletIds: ['w1', 'w2'],
        status: 'settled',
        from: '2026-01-01',
        to: '',
        cursor: 'abc=',
        limit: 50,
      })
      .subscribe();
    const req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/search'));
    const params = new URLSearchParams(req.request.urlWithParams.split('?')[1]);
    expect(Object.fromEntries(params)).toEqual({
      q: 'uber',
      kinds: 'expense,transfer',
      walletIds: 'w1,w2',
      status: 'settled',
      from: '2026-01-01',
      cursor: 'abc=',
      limit: '50',
    });
    req.flush({ items: [], totalCents: 0, totalCount: 0 });
  });

  it('search sem filtros não manda query string', () => {
    service.search({}).subscribe();
    http.expectOne('/api/v1/entries/search').flush({ items: [], totalCents: 0, totalCount: 0 });
  });
});
