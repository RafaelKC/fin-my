import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { sessao } from '../auth/testing';
import { WIDGET_BRIDGE, WidgetBridgePlugin, WidgetBridgeService } from './widget-bridge.service';

describe('WidgetBridgeService (010)', () => {
  let chamadas: string[];
  let conectado: boolean;

  function plugin(): WidgetBridgePlugin {
    return {
      status: async () => (chamadas.push('status'), { connected: conectado }),
      begin: async () => (
        chamadas.push('begin'),
        {
          clientId: 'finmy-android',
          redirectUri: 'br.app.finmy:/oauth/callback',
          codeChallenge: 'desafio',
        }
      ),
      complete: async ({ ticket }) => (chamadas.push(`complete:${ticket}`), { connected: true }),
      disconnect: async () => void chamadas.push('disconnect'),
      setAccent: async () => undefined,
      notificationStatus: async () => ({ listenerEnabled: false, apps: [] }),
      openNotificationAccess: async () => undefined,
      setBankApp: async () => undefined,
    };
  }

  function configurar(bridge: WidgetBridgePlugin | null) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: WIDGET_BRIDGE, useValue: bridge },
      ],
    });
    return {
      http: TestBed.inject(HttpTestingController),
      servico: TestBed.inject(WidgetBridgeService),
    };
  }

  beforeEach(() => {
    chamadas = [];
    conectado = false;
  });

  it('logado no app: pede o desafio, cria o ticket com a sessão e entrega ao nativo', async () => {
    const { http, servico } = configurar(plugin());
    TestBed.inject(AuthService).startSession(sessao());

    const pronto = servico.conectar();
    await new Promise((r) => setTimeout(r));
    const req = http.expectOne('/api/v1/oauth/consent-tickets');
    expect(req.request.body).toEqual({
      clientId: 'finmy-android',
      redirectUri: 'br.app.finmy:/oauth/callback',
      codeChallenge: 'desafio',
    });
    req.flush({ ticket: 'tk-1', expiresAt: '2026-11-25T10:02:00Z' });
    await pronto;

    expect(chamadas).toEqual(['status', 'begin', 'complete:tk-1']);
    http.verify();
  });

  it('já conectado: não cria ticket', async () => {
    conectado = true;
    const { http, servico } = configurar(plugin());

    await servico.conectar();

    expect(chamadas).toEqual(['status']);
    http.verify();
  });

  it('falha ao criar o ticket não quebra nada', async () => {
    const { http, servico } = configurar(plugin());

    const pronto = servico.conectar();
    await new Promise((r) => setTimeout(r));
    http
      .expectOne('/api/v1/oauth/consent-tickets')
      .flush(null, { status: 500, statusText: 'erro' });
    await pronto;

    expect(chamadas).toEqual(['status', 'begin']);
  });

  it('no navegador (sem plugin) não faz nada', async () => {
    const { http, servico } = configurar(null);

    await servico.conectar();
    await servico.desconectar();

    http.verify();
  });

  it('sair do app desconecta o widget', async () => {
    conectado = true;
    configurar(plugin());
    const auth = TestBed.inject(AuthService);
    auth.startSession(sessao());
    TestBed.tick();
    await new Promise((r) => setTimeout(r));

    auth.endSession();
    TestBed.tick();
    await new Promise((r) => setTimeout(r));

    expect(chamadas).toContain('disconnect');
  });
});
