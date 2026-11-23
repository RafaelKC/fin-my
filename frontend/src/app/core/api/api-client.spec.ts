import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiClient, url } from './api-client';

describe('ApiClient', () => {
  let api: ApiClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('usa /api/v1 como base, com ou sem barra inicial', () => {
    expect(url('/me')).toBe('/api/v1/me');
    expect(url('me')).toBe('/api/v1/me');
  });

  it('get chama a URL com a base da API', () => {
    let result: unknown;
    api.get('/me').subscribe((r) => (result = r));
    const req = http.expectOne('/api/v1/me');
    expect(req.request.method).toBe('GET');
    req.flush({ ok: true });
    expect(result).toEqual({ ok: true });
  });

  it('post envia o corpo; patch, put e delete usam o método certo', () => {
    api.post('/auth/login', { email: 'a@b.c' }).subscribe();
    const post = http.expectOne('/api/v1/auth/login');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ email: 'a@b.c' });
    post.flush({});

    api.patch('/me', { name: 'Ana' }).subscribe();
    expect(http.expectOne('/api/v1/me').request.method).toBe('PATCH');

    api.put('/x', {}).subscribe();
    expect(http.expectOne('/api/v1/x').request.method).toBe('PUT');

    api.delete('/x').subscribe();
    expect(http.expectOne('/api/v1/x').request.method).toBe('DELETE');
  });
});
