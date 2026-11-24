import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { PainelPage } from './painel.page';
import { agora, painel2026 } from './testing';

describe('PainelPage', () => {
  let http: HttpTestingController;
  const params = new BehaviorSubject(convertToParamMap({ ano: '2026' }));

  async function abrir(now = agora()) {
    const fixture = TestBed.createComponent(PainelPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/now').flush(now);
    http.expectOne('/api/v1/years/2026/panel').flush(painel2026());
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const limpo = (e: Element | null | undefined) => e?.textContent?.replace(/\s+/g, ' ').trim();
    const linha = (mes: string) =>
      el.querySelector(`[data-testid="painel-mes"][data-mes="${mes}"]`)!;
    return { fixture, el, limpo, linha };
  }

  beforeEach(() => {
    params.next(convertToParamMap({ ano: '2026' }));
    TestBed.configureTestingModule({
      imports: [PainelPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('US1.1: 12 linhas JAN–DEZ com os valores do Previsto e a linha de total', async () => {
    const { el, limpo, linha } = await abrir();
    expect(limpo(el.querySelector('[data-testid="painel-titulo"]'))).toBe('2026');
    expect(el.querySelectorAll('[data-testid="painel-mes"]')).toHaveLength(12);
    const jan = linha('2026-01');
    expect(limpo(jan.querySelector('th'))).toBe('JAN');
    expect(limpo(jan.querySelector('[data-testid="entradas"]'))).toContain('5.000,00');
    expect(limpo(jan.querySelector('[data-testid="saidas"]'))).toContain('2.000,00');
    expect(limpo(jan.querySelector('[data-testid="resultado"]'))).toContain('3.000,00');
    expect(limpo(jan.querySelector('[data-testid="saldo"]'))).toContain('3.000,00');
    const total = el.querySelector('[data-testid="painel-total"]')!;
    expect(limpo(total.querySelector('[data-testid="entradas"]'))).toContain('6.000,00');
    expect(limpo(total.querySelector('[data-testid="saidas"]'))).toContain('4.500,00');
    expect(limpo(total.querySelector('[data-testid="resultado"]'))).toContain('1.500,00');
    expect(limpo(total.querySelector('[data-testid="saldo"]'))).toContain('1.500,00');
  });

  it('US1.2: resultado e saldo negativos ficam em vermelho', async () => {
    const { linha } = await abrir();
    const fev = linha('2026-02');
    expect(fev.querySelector('[data-testid="resultado"]')!.classList).toContain('negativo');
    expect(fev.querySelector('[data-testid="saldo"]')!.classList).not.toContain('negativo');
    expect(linha('2026-01').querySelector('[data-testid="resultado"]')!.classList).not.toContain(
      'negativo',
    );
  });

  it('US1.3: tocar numa linha abre a visão do mês; o link não navega duas vezes', async () => {
    const { el, linha } = await abrir();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    linha('2026-02').querySelector<HTMLElement>('td')!.click();
    expect(navegar).toHaveBeenCalledWith(['/mes', '2026-02']);
    navegar.mockClear();
    el.querySelector<HTMLElement>('[data-testid="painel-mes-link"]')!.click();
    expect(navegar).not.toHaveBeenCalled();
    expect(
      linha('2026-03').querySelector('[data-testid="painel-mes-link"]')!.getAttribute('href'),
    ).toBe('/mes/2026-03');
  });

  it('US1.4: trocar para Realizado mostra os valores do Realizado', async () => {
    const { el, fixture, limpo, linha } = await abrir();
    el.querySelector<HTMLElement>('[data-testid="leitura-realizado"]')!.click();
    fixture.detectChanges();
    expect(limpo(linha('2026-01').querySelector('[data-testid="entradas"]'))).toContain('4.500,00');
    expect(
      limpo(el.querySelector('[data-testid="painel-total"] [data-testid="resultado"]')),
    ).toContain('2.500,00');
    expect(
      el.querySelector('[data-testid="leitura-realizado"]')!.getAttribute('aria-pressed'),
    ).toBe('true');
  });

  it('US1.5: o gráfico acompanha a leitura escolhida', async () => {
    const { el, fixture } = await abrir();
    const rotulo = () => el.querySelector('[data-testid="grafico"]')!.getAttribute('aria-label');
    expect(rotulo()).toBe('Previsto de 2026');
    expect(el.querySelectorAll('[data-testid="grafico-mes"]')).toHaveLength(12);
    el.querySelector<HTMLElement>('[data-testid="leitura-realizado"]')!.click();
    fixture.detectChanges();
    expect(rotulo()).toBe('Realizado de 2026');
  });

  it('US1.6: só o mês de hoje fica destacado', async () => {
    const { el, linha } = await abrir();
    expect(el.querySelectorAll('[data-testid="painel-mes"].hoje')).toHaveLength(1);
    expect(linha('2026-03').classList).toContain('hoje');
    expect(linha('2026-03').getAttribute('aria-current')).toBe('date');
  });

  it('US2.1: saldos de cada carteira e o total', async () => {
    const { el, limpo } = await abrir();
    const carteiras = Array.from(el.querySelectorAll('[data-testid="agora-carteira"]')).map(limpo);
    expect(carteiras[0]).toContain('Azul');
    expect(carteiras[0]).toContain('1.800,00');
    expect(carteiras[1]).toContain('150,00');
    expect(limpo(el.querySelector('[data-testid="agora-total"]'))).toContain('1.950,00');
    expect(el.querySelector('[data-testid="agora-arquivadas"]')).toBeNull();
  });

  it('carteiras arquivadas com saldo aparecem numa linha "Arquivadas"', async () => {
    const { el, limpo } = await abrir(agora({ archivedWalletsBalanceCents: 5000 }));
    expect(limpo(el.querySelector('[data-testid="agora-arquivadas"]'))).toContain('50,00');
  });

  it('US2.2: faturas com vencimento, situação e Restante, na ordem recebida', async () => {
    const { el, limpo } = await abrir();
    const faturas = Array.from(el.querySelectorAll('[data-testid="agora-fatura"]')).map(limpo);
    expect(faturas).toHaveLength(2);
    expect(faturas[0]).toContain('Azul');
    expect(faturas[0]).toContain('25/03/2026');
    expect(faturas[0]).toContain('Fechada');
    expect(faturas[0]).toContain('200,00');
    expect(faturas[1]).toContain('10/04/2026');
    expect(faturas[1]).toContain('Aberta');
    expect(faturas[1]).toContain('300,00');
    expect(el.querySelector('[data-testid="agora-fatura"] a')!.getAttribute('href')).toBe(
      '/cartoes/c-azul/faturas/2026-03',
    );
  });

  it('sem faturas próximas mostra o estado vazio', async () => {
    const { el } = await abrir(agora({ invoices: [] }));
    expect(el.querySelector('[data-testid="agora-sem-faturas"]')).not.toBeNull();
  });

  it('US2.3: pendências com quantidade, totais e acesso à lista do mês de hoje', async () => {
    const { el, limpo } = await abrir();
    expect(limpo(el.querySelector('[data-testid="agora-pendencias-qtd"]'))).toBe('2');
    expect(limpo(el.querySelector('[data-testid="agora-pendencias-receber"]'))).toContain('100,00');
    expect(limpo(el.querySelector('[data-testid="agora-pendencias-pagar"]'))).toContain('45,50');
    expect(el.querySelector('[data-testid="agora-ver-pendencias"]')!.getAttribute('href')).toBe(
      '/mes/2026-03',
    );
  });

  it('US2.4: "Bruna te deve R$ 170,00" e "Crédito da Carla: R$ 20,00"', async () => {
    const { el, limpo } = await abrir();
    const acertos = Array.from(el.querySelectorAll('[data-testid="agora-acerto"]')).map(limpo);
    expect(acertos[0]).toMatch(/^Bruna te deve R\$\s170,00$/);
    expect(acertos[1]).toMatch(/^Crédito da Carla: R\$\s20,00$/);
  });

  it('as setas trocam de ano', async () => {
    const { el } = await abrir();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    el.querySelector<HTMLElement>('[data-testid="painel-seguinte"]')!.click();
    expect(navegar).toHaveBeenCalledWith(['/painel', 2027]);
    el.querySelector<HTMLElement>('[data-testid="painel-anterior"]')!.click();
    expect(navegar).toHaveBeenCalledWith(['/painel', 2025]);
  });

  it('ano inválido volta para o ano de hoje', () => {
    params.next(convertToParamMap({ ano: '1999' }));
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(PainelPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/now').flush(agora());
    expect(navegar).toHaveBeenCalledOnce();
    expect(navegar.mock.calls[0][0][0]).toBe('/painel');
    http.expectNone('/api/v1/years/1999/panel');
  });

  it('erro do painel mostra a mensagem sem derrubar o Agora', async () => {
    const fixture = TestBed.createComponent(PainelPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/now').flush(agora());
    http
      .expectOne('/api/v1/years/2026/panel')
      .flush(
        { title: 'Falha', detail: 'Não foi possível carregar.' },
        { status: 500, statusText: 'x' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="erro-painel"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="agora-carteira"]')).not.toBeNull();
  });
});
