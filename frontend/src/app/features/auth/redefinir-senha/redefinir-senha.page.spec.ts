import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { problema } from '../../../core/auth/testing';
import { RedefinirSenhaPage } from './redefinir-senha.page';

describe('RedefinirSenhaPage', () => {
  let fixture: ComponentFixture<RedefinirSenhaPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  function abrir(query: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [RedefinirSenhaPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(query) } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RedefinirSenhaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  const enviar = (senha: string) => {
    const campo = el.querySelector<HTMLInputElement>('#senha')!;
    campo.value = senha;
    campo.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };

  afterEach(() => http.verify());

  it('sem token na URL mostra link inválido com "Pedir novo link"', () => {
    abrir({});

    expect(el.querySelector('[data-testid="link-invalido"]')).not.toBeNull();
    expect(el.querySelector('form')).toBeNull();
    const link = [...el.querySelectorAll('a')].find((a) => a.textContent === 'Pedir novo link');
    expect(link!.getAttribute('href')).toBe('/esqueci-senha');
  });

  it('senha com menos de 8 caracteres explica o requisito e não chama a API', () => {
    abrir({ token: 'abc' });
    enviar('1234567');

    expect(el.textContent).toContain('A senha precisa ter pelo menos 8 caracteres.');
    http.expectNone('/api/v1/auth/password-reset/confirm');
  });

  it('alterna mostrar/ocultar senha', () => {
    abrir({ token: 'abc' });
    const campo = el.querySelector<HTMLInputElement>('#senha')!;
    expect(campo.type).toBe('password');
    el.querySelector<HTMLButtonElement>('.mostrar')!.click();
    fixture.detectChanges();
    expect(campo.type).toBe('text');
  });

  it('envia token e nova senha e mostra a confirmação (204)', () => {
    abrir({ token: 'abc' });
    enviar('nova-senha-1');

    const req = http.expectOne('/api/v1/auth/password-reset/confirm');
    expect(req.request.body).toEqual({ token: 'abc', newPassword: 'nova-senha-1' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="senha-redefinida"]')).not.toBeNull();
    const link = [...el.querySelectorAll('a')].find((a) => a.textContent === 'Entrar');
    expect(link!.getAttribute('href')).toBe('/entrar');
  });

  it('410 reset_token_invalid mostra mensagem e botão "Pedir novo link"', () => {
    abrir({ token: 'velho' });
    enviar('nova-senha-1');
    const { body, opts } = problema(410, 'reset_token_invalid');
    http.expectOne('/api/v1/auth/password-reset/confirm').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="link-invalido"]')!.textContent).toContain('expirou');
    expect(el.textContent).toContain('Pedir novo link');
  });

  it('422 mostra a mensagem junto do campo', () => {
    abrir({ token: 'abc' });
    enviar('nova-senha-1');
    http.expectOne('/api/v1/auth/password-reset/confirm').flush(
      {
        status: 422,
        code: 'validation_failed',
        detail: 'Dados inválidos',
        errors: { newPassword: ['A senha é fraca demais.'] },
      },
      { status: 422, statusText: 'Unprocessable' },
    );
    fixture.detectChanges();

    expect(el.querySelector('#senha-erro')!.textContent).toContain('A senha é fraca demais.');
  });
});
