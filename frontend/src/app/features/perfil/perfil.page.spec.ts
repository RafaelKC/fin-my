import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problema, sessao } from '../../core/auth/testing';
import { PerfilPage } from './perfil.page';

describe('PerfilPage', () => {
  let fixture: ComponentFixture<PerfilPage>;
  let http: HttpTestingController;
  let auth: AuthService;
  let el: HTMLElement;

  const preencher = (id: string, valor: string) => {
    const campo = el.querySelector<HTMLInputElement>(id)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  };
  const enviar = (formulario: string) => {
    el.querySelector<HTMLFormElement>(formulario)!
      .querySelector<HTMLButtonElement>('button[type="submit"]')!
      .click();
    fixture.detectChanges();
  };
  const ids = (id: string) => `section[aria-labelledby="${id}"] form`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PerfilPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.startSession(sessao());
    fixture = TestBed.createComponent(PerfilPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/me/connected-apps').flush([]);
  });

  afterEach(() => http.verify());

  it('mostra e-mail e preenche nome e nome do Espaço atuais', () => {
    expect(el.querySelector('[data-testid="email-da-conta"]')!.textContent).toContain(
      'ana@example.com',
    );
    expect(el.querySelector<HTMLInputElement>('#nome')!.value).toBe('Ana');
    expect(el.querySelector<HTMLInputElement>('#espaco')!.value).toBe('Espaço da Ana');
  });

  it('tela inicial: Mês por padrão; escolher Painel grava a preferência do aparelho (FR-006)', () => {
    localStorage.removeItem('finmy.prefs.v1');
    const mes = el.querySelector<HTMLInputElement>('[data-testid="tela-inicial-mes"]')!;
    const painel = el.querySelector<HTMLInputElement>('[data-testid="tela-inicial-painel"]')!;
    expect(mes.checked).toBe(true);
    painel.click();
    fixture.detectChanges();
    expect(painel.checked).toBe(true);
    expect(JSON.parse(localStorage.getItem('finmy.prefs.v1')!).homeScreen).toBe('painel');
    mes.click();
    expect(JSON.parse(localStorage.getItem('finmy.prefs.v1')!).homeScreen).toBe('mes');
  });

  it('nome vazio explica o problema e não chama a API', () => {
    preencher('#nome', '   ');
    el.querySelector<HTMLInputElement>('#nome')!.value = '';
    el.querySelector('#nome')!.dispatchEvent(new Event('input'));
    enviar(ids('titulo-nome'));

    expect(el.textContent).toContain('Informe seu nome.');
    http.expectNone('/api/v1/me');
  });

  it('salva o nome (PATCH /me) e reflete na sessão', () => {
    preencher('#nome', ' Ana Souza ');
    enviar(ids('titulo-nome'));

    const req = http.expectOne('/api/v1/me');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ name: 'Ana Souza' });
    req.flush({
      user: { id: 'u1', name: 'Ana Souza', email: 'ana@example.com' },
      tenant: sessao().tenant,
    });
    fixture.detectChanges();

    expect(auth.user()?.name).toBe('Ana Souza');
    expect(el.querySelector('[data-testid="nome-salvo"]')).not.toBeNull();
  });

  it('renomeia o Espaço (PATCH /tenant) e o novo nome vai para a sessão (topo)', () => {
    preencher('#espaco', 'Casa da Ana');
    enviar(ids('titulo-espaco'));

    const req = http.expectOne('/api/v1/tenant');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ name: 'Casa da Ana' });
    req.flush({ id: 't1', name: 'Casa da Ana', role: 'owner' });
    fixture.detectChanges();

    expect(auth.tenant()?.name).toBe('Casa da Ana');
    expect(el.querySelector('[data-testid="espaco-salvo"]')).not.toBeNull();
  });

  it('erro de validação (422) do nome aparece e a sessão não muda', () => {
    preencher('#nome', 'Outra');
    enviar(ids('titulo-nome'));
    const { body, opts } = problema(422, 'validation_failed', 'Dados inválidos.');
    http.expectOne('/api/v1/me').flush({ ...body, errors: { name: ['Nome inválido.'] } }, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="erro-nome"]')!.textContent).toContain('Nome inválido.');
    expect(auth.user()?.name).toBe('Ana');
  });

  it('senha nova curta não chama a API', () => {
    preencher('#senha-atual', 'senha-antiga');
    preencher('#senha-nova', '1234567');
    enviar(ids('titulo-senha'));

    expect(el.textContent).toContain('A senha precisa ter pelo menos 8 caracteres.');
    http.expectNone('/api/v1/me/password');
  });

  it('troca a senha (204), limpa os campos e confirma', () => {
    preencher('#senha-atual', 'senha-antiga');
    preencher('#senha-nova', 'senha-nova-1');
    enviar(ids('titulo-senha'));

    const req = http.expectOne('/api/v1/me/password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      currentPassword: 'senha-antiga',
      newPassword: 'senha-nova-1',
    });
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="senha-trocada"]')!.textContent).toContain(
      'outras sessões foram encerradas',
    );
    expect(el.querySelector<HTMLInputElement>('#senha-atual')!.value).toBe('');
    expect(el.querySelector<HTMLInputElement>('#senha-nova')!.value).toBe('');
  });

  it('senha atual errada (422 current_password_incorrect) aparece junto do campo', () => {
    preencher('#senha-atual', 'errada-123');
    preencher('#senha-nova', 'senha-nova-1');
    enviar(ids('titulo-senha'));
    const { body, opts } = problema(422, 'current_password_incorrect', 'Senha atual incorreta.');
    http.expectOne('/api/v1/me/password').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('#senha-atual-erro')!.textContent).toContain('Senha atual incorreta.');
    expect(el.querySelector('[data-testid="senha-trocada"]')).toBeNull();
  });

  it('Sair chama /auth/logout e leva para /entrar', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    el.querySelector<HTMLButtonElement>('button.sair')!.click();

    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/entrar']);
  });
});
