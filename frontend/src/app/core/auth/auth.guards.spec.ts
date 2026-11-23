import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authGuard, publicGuard } from './auth.guards';
import { problema, sessao } from './testing';

@Component({ template: '' })
class Vazio {}

describe('guards de rota', () => {
  let router: Router;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: '', component: Vazio, canActivate: [authGuard] },
          { path: 'perfil', component: Vazio, canActivate: [authGuard] },
          { path: 'entrar', component: Vazio, canActivate: [publicGuard] },
        ]),
      ],
    });
    router = TestBed.inject(Router);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const navegar = async (url: string, refresh: 'ok' | 'falha') => {
    const nav = router.navigateByUrl(url);
    const req = await vi.waitFor(() => http.expectOne('/api/v1/auth/refresh'));
    if (refresh === 'ok') req.flush(sessao());
    else {
      const { body, opts } = problema(401, 'session_expired');
      req.flush(body, opts);
    }
    await nav;
    return router.url;
  };

  it('logada sem sessão: vai para /entrar guardando a rota pedida', async () => {
    expect(await navegar('/perfil', 'falha')).toBe('/entrar?returnUrl=%2Fperfil');
  });

  it('logada sem sessão em "/": vai para /entrar sem returnUrl', async () => {
    expect(await navegar('/', 'falha')).toBe('/entrar');
  });

  it('logada com refresh bem-sucedido na abertura: entra na rota pedida', async () => {
    expect(await navegar('/perfil', 'ok')).toBe('/perfil');
  });

  it('pública com sessão ativa: vai para "/"', async () => {
    expect(await navegar('/entrar', 'ok')).toBe('/');
  });

  it('pública sem sessão: permanece', async () => {
    expect(await navegar('/entrar', 'falha')).toBe('/entrar');
  });
});
