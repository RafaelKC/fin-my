import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { problema, sessao } from '../../../core/auth/testing';
import { CriarContaPage } from './criar-conta.page';

describe('CriarContaPage', () => {
  let fixture: ComponentFixture<CriarContaPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const input = (id: string) => el.querySelector<HTMLInputElement>(`#${id}`)!;
  const preencher = (nome: string, email: string, senha: string) => {
    for (const [id, valor] of [
      ['nome', nome],
      ['email', email],
      ['senha', senha],
    ]) {
      input(id).value = valor;
      input(id).dispatchEvent(new Event('input'));
    }
  };
  const enviar = () => {
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CriarContaPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CriarContaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('senha com menos de 8 caracteres explica o requisito e não chama a API', () => {
    preencher('Ana Souza', 'ana@example.com', '1234567');
    enviar();

    expect(el.textContent).toContain('A senha precisa ter pelo menos 8 caracteres.');
    http.expectNone('/api/v1/auth/signup');
  });

  it('alterna mostrar/ocultar senha', () => {
    expect(input('senha').type).toBe('password');
    el.querySelector<HTMLButtonElement>('.mostrar')!.click();
    fixture.detectChanges();
    expect(input('senha').type).toBe('text');
    expect(el.querySelector('.mostrar')!.getAttribute('aria-label')).toBe('Ocultar senha');
  });

  it('cria a conta, inicia a sessão e navega para "/"', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    preencher(' Ana Souza ', ' ana@example.com ', 'senha-segura');
    enviar();

    const req = http.expectOne('/api/v1/auth/signup');
    expect(req.request.body).toEqual({
      name: 'Ana Souza',
      email: 'ana@example.com',
      password: 'senha-segura',
    });
    req.flush(sessao(), { status: 201, statusText: 'Created' });

    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('409 mostra a mensagem do cenário 1.2 com atalhos para entrar e recuperar senha', () => {
    preencher('Ana', 'ana@example.com', 'senha-segura');
    enviar();
    const { body, opts } = problema(
      409,
      'email_already_registered',
      'Este e-mail já tem conta. Entrar ou recuperar senha?',
    );
    http.expectOne('/api/v1/auth/signup').flush(body, opts);
    fixture.detectChanges();

    const aviso = el.querySelector('[data-testid="email-ja-cadastrado"]')!;
    expect(aviso.textContent).toContain('Este e-mail já tem conta. Entrar ou recuperar senha?');
    const hrefs = [...aviso.querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/entrar', '/esqueci-senha']);
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
  });

  it('422 mostra as mensagens por campo', () => {
    preencher('Ana', 'ana@example.com', 'senha-segura');
    enviar();
    http.expectOne('/api/v1/auth/signup').flush(
      {
        status: 422,
        code: 'validation_failed',
        detail: 'Dados inválidos',
        errors: { email: ['Informe um e-mail válido.'] },
      },
      { status: 422, statusText: 'Unprocessable' },
    );
    fixture.detectChanges();

    expect(el.querySelector('#email-erro')?.textContent).toContain('Informe um e-mail válido.');
  });
});
