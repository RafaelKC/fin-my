import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problema } from '../../../core/auth/testing';
import { EsqueciSenhaPage } from './esqueci-senha.page';

describe('EsqueciSenhaPage', () => {
  let fixture: ComponentFixture<EsqueciSenhaPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const enviar = (email: string) => {
    const campo = el.querySelector<HTMLInputElement>('#email')!;
    campo.value = email;
    campo.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EsqueciSenhaPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EsqueciSenhaPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('e-mail inválido explica o problema e não chama a API', () => {
    enviar('sem-arroba');

    expect(el.textContent).toContain('Informe um e-mail válido.');
    http.expectNone('/api/v1/auth/password-reset/request');
  });

  it('pede o link e mostra sempre a mesma confirmação (202)', () => {
    enviar(' ana@example.com ');

    const req = http.expectOne('/api/v1/auth/password-reset/request');
    expect(req.request.body).toEqual({ email: 'ana@example.com' });
    req.flush(null, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="link-enviado"]')!.textContent).toContain(
      'Se houver conta com este e-mail, enviaremos um link',
    );
    expect(el.querySelector('form')).toBeNull();
  });

  it('erro da API (429) mostra a mensagem e mantém o formulário', () => {
    enviar('ana@example.com');
    const { body, opts } = problema(429, 'rate_limited', 'Muitas tentativas. Tente mais tarde.');
    http.expectOne('/api/v1/auth/password-reset/request').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="erro-geral"]')!.textContent).toContain(
      'Muitas tentativas. Tente mais tarde.',
    );
    expect(el.querySelector('form')).not.toBeNull();
  });
});
