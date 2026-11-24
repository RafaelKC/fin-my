import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { problema } from '../../core/auth/testing';
import { AplicativosConectados, AplicativoConectado } from './aplicativos-conectados';

const CLAUDE: AplicativoConectado = {
  id: 'auth-1',
  name: 'Claude',
  connectedAt: '2026-11-20T13:00:00Z',
  lastUsedAt: '2026-11-23T15:30:00Z',
};
const NOVO: AplicativoConectado = {
  id: 'auth-2',
  name: 'Claude Celular',
  connectedAt: '2026-11-24T10:00:00Z',
  lastUsedAt: null,
};

describe('AplicativosConectados (008 US1.2)', () => {
  let fixture: ComponentFixture<AplicativosConectados>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const itens = () => Array.from(el.querySelectorAll('[data-testid="aplicativo-conectado"]'));
  const botao = (texto: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.includes(texto),
    )!;

  function abrir(resposta: AplicativoConectado[]) {
    fixture = TestBed.createComponent(AplicativosConectados);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/me/connected-apps').flush(resposta);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lista nome, desde quando está conectado e o último uso', () => {
    abrir([CLAUDE, NOVO]);

    expect(itens()).toHaveLength(2);
    expect(itens()[0].textContent).toContain('Claude');
    expect(itens()[0].textContent).toContain('Conectado em');
    expect(itens()[0].querySelector('[data-testid="ultimo-uso"]')!.textContent).toContain(
      'Último uso em',
    );
    expect(itens()[1].querySelector('[data-testid="ultimo-uso"]')!.textContent).toContain(
      'Ainda não usado',
    );
  });

  it('sem conexões explica como conectar', () => {
    abrir([]);

    expect(el.querySelector('[data-testid="sem-aplicativos"]')).not.toBeNull();
    expect(itens()).toHaveLength(0);
  });

  it('desconectar pede confirmação e só então chama a API', () => {
    abrir([CLAUDE, NOVO]);

    botao('Desconectar').click();
    fixture.detectChanges();
    http.expectNone('/api/v1/me/connected-apps/auth-1');
    expect(el.textContent).toContain('perde o acesso na hora');

    botao('Sim, desconectar').click();
    fixture.detectChanges();
    const req = http.expectOne('/api/v1/me/connected-apps/auth-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(itens()).toHaveLength(1);
    expect(itens()[0].textContent).toContain('Claude Celular');
  });

  it('manter desfaz o pedido de desconexão sem chamar a API', () => {
    abrir([CLAUDE]);

    botao('Desconectar').click();
    fixture.detectChanges();
    botao('Manter').click();
    fixture.detectChanges();

    expect(itens()).toHaveLength(1);
    expect(el.textContent).not.toContain('perde o acesso na hora');
  });

  it('falha ao desconectar mostra o erro e mantém o aplicativo na lista', () => {
    abrir([CLAUDE]);

    botao('Desconectar').click();
    fixture.detectChanges();
    botao('Sim, desconectar').click();
    const { body, opts } = problema(404, 'connected_app_not_found', 'Aplicativo não encontrado.');
    http.expectOne('/api/v1/me/connected-apps/auth-1').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="erro-aplicativos"]')!.textContent).toContain(
      'Aplicativo não encontrado.',
    );
    expect(itens()).toHaveLength(1);
  });

  it('falha ao listar mostra o erro', () => {
    fixture = TestBed.createComponent(AplicativosConectados);
    el = fixture.nativeElement;
    fixture.detectChanges();
    const { body, opts } = problema(500, 'internal_error', 'Algo deu errado.');
    http.expectOne('/api/v1/me/connected-apps').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="erro-aplicativos"]')).not.toBeNull();
  });
});
