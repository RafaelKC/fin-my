import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { today } from '../../core/money';
import { carteira } from '../carteiras/testing';
import { OnboardingState } from './onboarding.models';
import { PrimeirosPassosPage } from './primeiros-passos.page';

const vazio = (parcial: Partial<OnboardingState> = {}): OnboardingState => ({
  steps: {},
  dismissed: false,
  autoStart: true,
  ...parcial,
});

describe('PrimeirosPassosPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<PrimeirosPassosPage>;
  let el: HTMLElement;
  let navegar: ReturnType<typeof vi.spyOn>;
  const params = new BehaviorSubject(convertToParamMap({ etapa: 'carteiras' }));

  /** Os formulários embutidos pedem carteiras, cartões e tags para os seletores. */
  function responderListas(carteiras: unknown[] = []) {
    http.match((r) => r.url.startsWith('/api/v1/wallets?')).forEach((r) => r.flush(carteiras));
    http.match((r) => /\/api\/v1\/(credit-cards|tags)\?/.test(r.url)).forEach((r) => r.flush([]));
  }

  function abrir(etapa: string, estado: OnboardingState = vazio(), carteiras: unknown[] = []) {
    params.next(convertToParamMap({ etapa }));
    fixture = TestBed.createComponent(PrimeirosPassosPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/onboarding').flush(estado);
    fixture.detectChanges();
    responderListas(carteiras);
    fixture.detectChanges();
  }
  const clicar = (testid: string) => {
    el.querySelector<HTMLButtonElement>(`[data-testid="${testid}"]`)!.click();
    fixture.detectChanges();
  };
  const texto = (testid: string) =>
    el.querySelector(`[data-testid="${testid}"]`)?.textContent?.replace(/\s+/g, ' ').trim();
  const estabilizar = async () => {
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PrimeirosPassosPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: params, snapshot: { paramMap: convertToParamMap({}) } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => http.verify());

  it('US3.1: mostra as 5 etapas e começa em Carteiras com o texto do saldo de hoje (US3.4)', () => {
    abrir('carteiras');
    expect(el.querySelectorAll('[data-testid="etapa-trilha"]')).toHaveLength(5);
    expect(texto('progresso')).toBe('Etapa 1 de 5');
    expect(texto('etapa-titulo')).toBe('Carteiras');
    expect(texto('etapa-explicacao')).toContain('informe o saldo que está na conta hoje');
    // formulário de carteira reaproveitado, com a data de hoje sugerida
    expect(el.querySelector<HTMLInputElement>('#data-saldo')!.value).toBe(today());
    expect(el.querySelector('h1')?.textContent).toBe('Primeiros passos');
  });

  it('US3.4: cadastrar a carteira fica na etapa, conta o cadastro e limpa o formulário', async () => {
    abrir('carteiras');
    const nome = el.querySelector<HTMLInputElement>('#nome')!;
    nome.value = 'Azul';
    nome.dispatchEvent(new Event('input'));
    el.querySelector<HTMLInputElement>('#saldo')!.value = '1800,00';
    el.querySelector<HTMLInputElement>('#saldo')!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));

    const req = http.expectOne({ method: 'POST', url: '/api/v1/wallets' });
    expect(req.request.body).toMatchObject({
      name: 'Azul',
      initialBalance: '1800,00',
      initialBalanceDate: today(),
    });
    req.flush(carteira());
    await estabilizar();

    expect(navegar).not.toHaveBeenCalled();
    expect(texto('etapa-cadastrados')).toContain('1 item cadastrado');
    // o formulário é religado logo depois, já limpo
    await new Promise((r) => setTimeout(r));
    fixture.detectChanges();
    expect(el.querySelector<HTMLInputElement>('#nome')!.value).toBe('');
  });

  it('Continuar marca a etapa como concluída e abre a próxima', () => {
    abrir('carteiras');
    clicar('continuar');
    const req = http.expectOne({ method: 'PUT', url: '/api/v1/onboarding/steps/wallets' });
    expect(req.request.body).toEqual({ state: 'done' });
    req.flush(vazio({ steps: { wallets: 'done' } }));
    expect(navegar).toHaveBeenCalledWith('/primeiros-passos/cartoes', { replaceUrl: false });
  });

  it('US3.2: Pular marca como pulada; na última etapa cai no mês de hoje', () => {
    abrir('contas-fixas');
    expect(texto('continuar')).toBe('Concluir');
    clicar('pular');
    const req = http.expectOne({ method: 'PUT', url: '/api/v1/onboarding/steps/recurrences' });
    expect(req.request.body).toEqual({ state: 'skipped' });
    req.flush(vazio());
    expect(navegar).toHaveBeenCalledWith(`/mes/${today().slice(0, 7)}`);
  });

  it('Pular numa etapa do meio vai para a seguinte', () => {
    abrir('cartoes', vazio(), [carteira()]);
    clicar('pular');
    http.expectOne({ method: 'PUT', url: '/api/v1/onboarding/steps/cards' }).flush(vazio());
    expect(navegar).toHaveBeenCalledWith('/primeiros-passos/pessoas', { replaceUrl: false });
  });

  it('US3.3: Dispensar guia chama a API e leva ao mês de hoje', () => {
    abrir('carteiras');
    clicar('dispensar');
    http
      .expectOne({ method: 'POST', url: '/api/v1/onboarding/dismiss' })
      .flush(vazio({ dismissed: true, autoStart: false }));
    expect(navegar).toHaveBeenCalledWith(`/mes/${today().slice(0, 7)}`);
  });

  it('pessoas: cria uma Tag de acerto e lista o nome cadastrado', async () => {
    abrir('pessoas');
    const campo = el.querySelector<HTMLInputElement>('#pessoa-nome')!;
    campo.value = 'Bruna';
    campo.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const req = http.expectOne({ method: 'POST', url: '/api/v1/tags' });
    expect(req.request.body).toEqual({ name: 'Bruna', isSettlement: true });
    req.flush({ id: 't-bruna', name: 'Bruna', isSettlement: true });
    await estabilizar();
    expect(texto('pessoas-cadastradas')).toBe('Bruna');
  });

  it('pessoas: nome repetido mostra o erro e não conta como cadastro', async () => {
    abrir('pessoas');
    const campo = el.querySelector<HTMLInputElement>('#pessoa-nome')!;
    campo.value = 'Bruna';
    campo.dispatchEvent(new Event('input'));
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/v1/tags')
      .flush(
        { status: 409, code: 'tag.name_taken', detail: 'x' },
        { status: 409, statusText: 'Conflict' },
      );
    await estabilizar();
    expect(texto('erro-pessoa')).toBe('Já existe uma tag com esse nome.');
    expect(el.querySelector('[data-testid="etapa-cadastrados"]')).toBeNull();
  });

  it('etapa desconhecida abre a primeira pendente', () => {
    params.next(convertToParamMap({ etapa: 'nada' }));
    fixture = TestBed.createComponent(PrimeirosPassosPage);
    fixture.detectChanges();
    const gets = http.match('/api/v1/onboarding');
    expect(gets).toHaveLength(2);
    gets.forEach((g) => g.flush(vazio({ steps: { wallets: 'done' } })));
    expect(navegar).toHaveBeenCalledWith('/primeiros-passos/cartoes', { replaceUrl: true });
  });

  it('falha ao marcar a etapa mostra o erro e não avança', () => {
    abrir('carteiras');
    clicar('continuar');
    http
      .expectOne('/api/v1/onboarding/steps/wallets')
      .flush(
        { status: 500, code: 'internal', detail: 'Falhou.' },
        { status: 500, statusText: 'x' },
      );
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro-guia"]')).not.toBeNull();
    expect(navegar).not.toHaveBeenCalled();
  });
});
