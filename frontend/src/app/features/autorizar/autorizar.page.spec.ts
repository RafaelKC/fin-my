import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problema, sessao } from '../../core/auth/testing';
import { PageNavigator } from '../../core/navigation/page-navigator';
import { AutorizarPage } from './autorizar.page';

const PEDIDO = {
  response_type: 'code',
  client_id: 'client-1',
  redirect_uri: 'https://claude.ai/api/mcp/auth_callback',
  code_challenge: 'abc123-challenge',
  code_challenge_method: 'S256',
  state: 'estado 1',
  resource: 'http://localhost/mcp',
};

describe('AutorizarPage (008 US1.1)', () => {
  let http: HttpTestingController;
  let destinos: string[];
  let fixture: ComponentFixture<AutorizarPage>;
  let el: HTMLElement;

  const botao = (texto: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.trim().startsWith(texto),
    )!;

  function abrir(params: Record<string, string | string[]> = PEDIDO) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(params) } },
        },
        { provide: PageNavigator, useValue: { go: (url: string) => destinos.push(url) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).startSession(sessao());
    fixture = TestBed.createComponent(AutorizarPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  beforeEach(() => {
    destinos = [];
  });

  afterEach(() => http.verify());

  it('mostra o consentimento com o nome do Espaço', () => {
    abrir();

    expect(el.querySelector('[data-testid="texto-consentimento"]')!.textContent).toMatch(
      /O Claude poderá ver e registrar seus lançamentos no Espaço\s+Espaço da Ana\./,
    );
  });

  it('Autorizar pede o ticket com o pedido e navega a página inteira com todos os parâmetros e o ticket', () => {
    abrir();

    botao('Autorizar').click();
    fixture.detectChanges();
    const req = http.expectOne('/api/v1/oauth/consent-tickets');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      clientId: 'client-1',
      redirectUri: 'https://claude.ai/api/mcp/auth_callback',
      codeChallenge: 'abc123-challenge',
    });
    req.flush({ ticket: 'ticket-opaco', expiresAt: '2026-11-24T10:02:00Z' });

    expect(destinos).toHaveLength(1);
    const url = new URL(destinos[0], 'http://localhost');
    expect(url.pathname).toBe('/oauth/authorize');
    for (const [chave, valor] of Object.entries(PEDIDO)) {
      expect(url.searchParams.get(chave)).toBe(valor);
    }
    expect(url.searchParams.get('consent_ticket')).toBe('ticket-opaco');
    expect(url.searchParams.has('consent_denied')).toBe(false);
  });

  it('Cancelar navega a página inteira para o authorize negando, sem pedir ticket', () => {
    abrir();

    botao('Cancelar').click();

    http.expectNone('/api/v1/oauth/consent-tickets');
    const url = new URL(destinos[0], 'http://localhost');
    expect(url.pathname).toBe('/oauth/authorize');
    expect(url.searchParams.get('consent_denied')).toBe('1');
    expect(url.searchParams.get('state')).toBe('estado 1');
    expect(url.searchParams.has('consent_ticket')).toBe(false);
  });

  it('não repassa um consent_ticket que já viesse no endereço', () => {
    abrir({ ...PEDIDO, consent_ticket: 'antigo' });

    botao('Cancelar').click();

    expect(new URL(destinos[0], 'http://localhost').searchParams.has('consent_ticket')).toBe(false);
  });

  it('erro ao pedir o ticket mostra a mensagem, não navega e libera de novo', () => {
    abrir();

    botao('Autorizar').click();
    const { body, opts } = problema(
      422,
      'oauth_client_unknown',
      'Aplicativo desconhecido. Adicione o conector de novo no Claude.',
    );
    http.expectOne('/api/v1/oauth/consent-tickets').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="erro-autorizar"]')!.textContent).toContain(
      'Aplicativo desconhecido',
    );
    expect(destinos).toHaveLength(0);
    expect(botao('Autorizar').disabled).toBe(false);
  });

  it('pedido sem os parâmetros do OAuth explica e não oferece autorizar', () => {
    abrir({ client_id: 'client-1' });

    expect(el.querySelector('[data-testid="pedido-invalido"]')).not.toBeNull();
    expect(el.querySelectorAll('button')).toHaveLength(0);
  });

  it('Claude: título e texto atuais', () => {
    abrir();

    expect(el.querySelector('h1')!.textContent).toBe('Autorizar o Claude');
  });

  it('finmy-android: título e texto próprios, sem mencionar o Claude, e o fluxo é o mesmo', () => {
    abrir({ ...PEDIDO, client_id: 'finmy-android', redirect_uri: 'br.app.finmy:/oauth2redirect' });

    expect(el.querySelector('h1')!.textContent).toBe('Autorizar o App Fin-My (Android)');
    const texto = el.querySelector('[data-testid="texto-consentimento"]')!.textContent!;
    expect(texto.replace(/\s+/g, ' ')).toBe(
      ' O App Fin-My (Android) poderá ver seus cartões e faturas no Espaço Espaço da Ana para mostrar no widget. ',
    );
    expect(el.textContent).not.toContain('Claude');

    botao('Autorizar').click();
    const req = http.expectOne('/api/v1/oauth/consent-tickets');
    expect(req.request.body).toMatchObject({ clientId: 'finmy-android' });
  });
});
