import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { problema, sessao } from './testing';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'perfil', children: [] }]),
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  it('começa sem sessão', () => {
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken).toBeNull();
    expect(auth.tenant()).toBeNull();
  });

  it('restore: refresh na abertura com sucesso ativa a sessão só em memória', async () => {
    const done = auth.restore();
    const req = http.expectOne('/api/v1/auth/refresh');
    expect(req.request.method).toBe('POST');
    req.flush(sessao('abc'));
    await done;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken).toBe('abc');
    expect(auth.user()?.name).toBe('Ana');
    expect(auth.tenant()?.name).toBe('Espaço da Ana');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('restore: falha no refresh deixa sem sessão e não rejeita', async () => {
    const done = auth.restore();
    const { body, opts } = problema(401, 'session_expired');
    http.expectOne('/api/v1/auth/refresh').flush(body, opts);
    await expect(done).resolves.toBeUndefined();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('restore: só tenta uma vez mesmo se chamado de novo', async () => {
    const a = auth.restore();
    const b = auth.restore();
    http.expectOne('/api/v1/auth/refresh').flush(sessao());
    await Promise.all([a, b]);
    await auth.restore();
    http.expectNone('/api/v1/auth/refresh');
  });

  it('refresh: chamadas simultâneas compartilham uma única requisição', () => {
    const tokens: string[] = [];
    auth.refresh().subscribe((s) => tokens.push(s.accessToken));
    auth.refresh().subscribe((s) => tokens.push(s.accessToken));
    http.expectOne('/api/v1/auth/refresh').flush(sessao('novo'));
    expect(tokens).toEqual(['novo', 'novo']);
    expect(auth.accessToken).toBe('novo');
  });

  it('refresh: depois de concluído, um novo refresh faz nova requisição', () => {
    auth.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(sessao('a'));
    auth.refresh().subscribe();
    http.expectOne('/api/v1/auth/refresh').flush(sessao('b'));
    expect(auth.accessToken).toBe('b');
  });

  it('refresh recusado (401) encerra a sessão e vai para /entrar', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    auth.startSession(sessao());
    let falhou = false;
    auth.refresh().subscribe({ error: () => (falhou = true) });
    const { body, opts } = problema(401, 'session_expired');
    http.expectOne('/api/v1/auth/refresh').flush(body, opts);

    expect(falhou).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/entrar'], { queryParams: {} });
  });

  it('refresh com erro de rede não derruba a sessão', () => {
    auth.startSession(sessao());
    auth.refresh().subscribe({ error: () => undefined });
    http.expectOne('/api/v1/auth/refresh').error(new ProgressEvent('error'));
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('endSession guarda a rota atual como returnUrl', async () => {
    await router.navigateByUrl('/perfil');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    auth.startSession(sessao());
    auth.endSession();
    expect(navigate).toHaveBeenCalledWith(['/entrar'], { queryParams: { returnUrl: '/perfil' } });
  });

  it('updateProfile troca usuário e Espaço na sessão em memória', () => {
    auth.startSession(sessao());
    auth.updateProfile({ user: { id: 'u1', name: 'Ana Souza', email: 'ana@example.com' } });
    auth.updateProfile({ tenant: { id: 't1', name: 'Casa', role: 'owner' } });
    expect(auth.user()?.name).toBe('Ana Souza');
    expect(auth.tenant()?.name).toBe('Casa');
    expect(auth.accessToken).toBe('token-1');
  });

  it('logout chama /auth/logout, descarta a sessão e vai para /entrar sem returnUrl', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    auth.startSession(sessao());
    auth.logout().subscribe();
    const req = http.expectOne('/api/v1/auth/logout');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/entrar']);
  });

  it('logout descarta a sessão local mesmo se a API falhar', () => {
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    auth.startSession(sessao());
    auth.logout().subscribe({ error: () => undefined });
    http.expectOne('/api/v1/auth/logout').error(new ProgressEvent('error'));
    expect(auth.isAuthenticated()).toBe(false);
  });
});
