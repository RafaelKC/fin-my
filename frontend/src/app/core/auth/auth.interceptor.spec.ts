import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { problema, sessao } from './testing';

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    auth.startSession(sessao('velho'));
  });

  afterEach(() => http.verify());

  const falhar = (req: TestRequest, status: number, code: string) => {
    const { body, opts } = problema(status, code);
    req.flush(body, opts);
  };

  it('anexa o access token nas chamadas da API', () => {
    client.get('/api/v1/me').subscribe();
    const req = http.expectOne('/api/v1/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer velho');
    req.flush({});
  });

  it('não anexa token em /auth/* nem em outras origens', () => {
    client.post('/api/v1/auth/login', {}).subscribe();
    expect(http.expectOne('/api/v1/auth/login').request.headers.has('Authorization')).toBe(false);

    client.get('https://example.com/x').subscribe();
    expect(http.expectOne('https://example.com/x').request.headers.has('Authorization')).toBe(
      false,
    );
  });

  it('em 401 token_expired renova uma vez e repete a requisição com o novo token', () => {
    let resultado: unknown;
    client.get('/api/v1/me').subscribe((r) => (resultado = r));

    falhar(http.expectOne('/api/v1/me'), 401, 'token_expired');
    http.expectOne('/api/v1/auth/refresh').flush(sessao('novo'));

    const repetida = http.expectOne('/api/v1/me');
    expect(repetida.request.headers.get('Authorization')).toBe('Bearer novo');
    repetida.flush({ ok: true });
    expect(resultado).toEqual({ ok: true });
  });

  it('com requisições simultâneas expiradas faz UM refresh e repete todas', () => {
    const resultados: unknown[] = [];
    client.get('/api/v1/a').subscribe((r) => resultados.push(r));
    client.get('/api/v1/b').subscribe((r) => resultados.push(r));
    client.get('/api/v1/c').subscribe((r) => resultados.push(r));

    falhar(http.expectOne('/api/v1/a'), 401, 'token_expired');
    falhar(http.expectOne('/api/v1/b'), 401, 'token_expired');
    falhar(http.expectOne('/api/v1/c'), 401, 'token_expired');

    // um único refresh, mesmo com três 401
    http.expectOne('/api/v1/auth/refresh').flush(sessao('novo'));
    http.expectNone('/api/v1/auth/refresh');

    for (const caminho of ['a', 'b', 'c']) {
      const repetida = http.expectOne(`/api/v1/${caminho}`);
      expect(repetida.request.headers.get('Authorization')).toBe('Bearer novo');
      repetida.flush(caminho);
    }
    expect(resultados).toEqual(['a', 'b', 'c']);
  });

  it('uma requisição que chega depois do refresh já usa o token novo', () => {
    client.get('/api/v1/a').subscribe();
    falhar(http.expectOne('/api/v1/a'), 401, 'token_expired');
    http.expectOne('/api/v1/auth/refresh').flush(sessao('novo'));
    http.expectOne('/api/v1/a').flush({});

    client.get('/api/v1/b').subscribe();
    const req = http.expectOne('/api/v1/b');
    expect(req.request.headers.get('Authorization')).toBe('Bearer novo');
    req.flush({});
  });

  it('se o refresh for recusado, propaga o erro, encerra a sessão e não repete', () => {
    let erro: unknown;
    client.get('/api/v1/me').subscribe({ error: (e) => (erro = e) });
    falhar(http.expectOne('/api/v1/me'), 401, 'token_expired');
    falhar(http.expectOne('/api/v1/auth/refresh'), 401, 'session_revoked');

    http.expectNone('/api/v1/me');
    expect(erro).toBeDefined();
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('401 session_revoked encerra a sessão e vai para o login sem tentar refresh', () => {
    let erro: unknown;
    client.get('/api/v1/me').subscribe({ error: (e) => (erro = e) });
    falhar(http.expectOne('/api/v1/me'), 401, 'session_revoked');

    http.expectNone('/api/v1/auth/refresh');
    expect(erro).toBeDefined();
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/entrar'], expect.anything());
  });

  it('não entra em laço: 401 token_expired na repetição vira erro', () => {
    let erro: unknown;
    client.get('/api/v1/me').subscribe({ error: (e) => (erro = e) });
    falhar(http.expectOne('/api/v1/me'), 401, 'token_expired');
    http.expectOne('/api/v1/auth/refresh').flush(sessao('novo'));
    falhar(http.expectOne('/api/v1/me'), 401, 'token_expired');

    http.expectNone('/api/v1/auth/refresh');
    expect(erro).toBeDefined();
  });

  it('outros erros passam direto, sem refresh', () => {
    let status = 0;
    client.get('/api/v1/me').subscribe({ error: (e) => (status = e.status) });
    falhar(http.expectOne('/api/v1/me'), 403, 'forbidden');
    http.expectNone('/api/v1/auth/refresh');
    expect(status).toBe(403);
    expect(auth.isAuthenticated()).toBe(true);
  });
});
