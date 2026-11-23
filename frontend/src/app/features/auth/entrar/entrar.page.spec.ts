import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { problema, sessao } from '../../../core/auth/testing';
import { EntrarPage, formatarTempoRestante, rotaDeRetorno } from './entrar.page';

describe('EntrarPage', () => {
  let fixture: ComponentFixture<EntrarPage>;
  let http: HttpTestingController;
  let router: Router;
  let el: HTMLElement;

  const input = (id: string) => el.querySelector<HTMLInputElement>(`#${id}`)!;
  const preencher = (email: string, senha: string) => {
    for (const [id, valor] of [
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
  const criar = async (url = '/entrar') => {
    router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    fixture = TestBed.createComponent(EntrarPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EntrarPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', component: EntrarPage }]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('campos vazios não chamam a API', async () => {
    await criar();
    enviar();

    expect(el.textContent).toContain('Informe seu e-mail.');
    expect(el.textContent).toContain('Informe sua senha.');
    http.expectNone('/api/v1/auth/login');
  });

  it('entra, inicia a sessão e vai para "/" sem returnUrl', async () => {
    await criar();
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    preencher(' ana@example.com ', 'senha-segura');
    enviar();

    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.body).toEqual({ email: 'ana@example.com', password: 'senha-segura' });
    req.flush(sessao());

    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('volta à rota pedida (returnUrl) depois do login', async () => {
    await criar('/entrar?returnUrl=%2Fperfil');
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    preencher('ana@example.com', 'senha-segura');
    enviar();
    http.expectOne('/api/v1/auth/login').flush(sessao());

    expect(navigate).toHaveBeenCalledWith('/perfil');
  });

  it('mostra a mensagem única da API em credenciais inválidas', async () => {
    await criar();
    preencher('ana@example.com', 'errada-123');
    enviar();
    const { body, opts } = problema(401, 'invalid_credentials', 'E-mail ou senha incorretos');
    http.expectOne('/api/v1/auth/login').flush(body, opts);
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="erro-geral"]')!.textContent).toContain(
      'E-mail ou senha incorretos',
    );
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
  });

  it('bloqueio mostra o tempo restante, desabilita o botão e libera ao zerar', async () => {
    vi.useFakeTimers();
    await criar();
    preencher('ana@example.com', 'senha-segura');
    enviar();
    const { body, opts } = problema(429, 'login_locked', 'Muitas tentativas.');
    http.expectOne('/api/v1/auth/login').flush({ ...body, retryAfterSeconds: 90 }, opts);
    fixture.detectChanges();

    const aviso = el.querySelector('[data-testid="bloqueio"]')!;
    expect(aviso.textContent).toContain('1 min 30 s');
    expect(el.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);

    vi.advanceTimersByTime(30_000);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="bloqueio"]')!.textContent).toContain('1 min 0 s');

    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="bloqueio"]')).toBeNull();
    expect(el.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
  });

  it('tem links para criar conta e recuperar senha', async () => {
    await criar();
    const hrefs = [...el.querySelectorAll('.rodape a')].map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/esqueci-senha', '/criar-conta']);
  });
});

describe('rotaDeRetorno', () => {
  it('aceita só rotas internas', () => {
    expect(rotaDeRetorno('/perfil')).toBe('/perfil');
    expect(rotaDeRetorno(null)).toBe('/');
    expect(rotaDeRetorno('https://evil.example')).toBe('/');
    expect(rotaDeRetorno('//evil.example')).toBe('/');
    expect(rotaDeRetorno('/\\evil.example')).toBe('/');
  });
});

describe('formatarTempoRestante', () => {
  it('formata minutos e segundos', () => {
    expect(formatarTempoRestante(900)).toBe('15 min 0 s');
    expect(formatarTempoRestante(59.2)).toBe('1 min 0 s');
    expect(formatarTempoRestante(45)).toBe('45 s');
  });
});
