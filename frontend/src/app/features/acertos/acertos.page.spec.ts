import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { today } from '../../core/money';
import { EntriesService } from '../lancamento/entries.service';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { AcertosPage } from './acertos.page';
import { SettlementItem } from './settlement.models';
import { linha } from './testing';

describe('AcertosPage', () => {
  let http: HttpTestingController;
  const params = new BehaviorSubject(convertToParamMap({ mes: '2026-03' }));

  async function abrir(linhas = [linha()]) {
    const fixture = TestBed.createComponent(AcertosPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/settlements/2026-03').flush(linhas);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const texto = (id: string) =>
      el.querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();
    return { fixture, el, texto };
  }

  beforeEach(() => {
    params.next(convertToParamMap({ mes: '2026-03' }));
    TestBed.configureTestingModule({
      imports: [AcertosPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('US2.1: mostra os números da Bruna e o título do mês', async () => {
    const { el, texto } = await abrir();
    expect(texto('acertos-titulo')).toBe('Março de 2026');
    const cartao = el.querySelector('[data-testid="acerto"]')!.textContent!.replace(/\s+/g, ' ');
    for (const esperado of ['200,00', '180,00', '30,00', '20,00', '170,00']) {
      expect(cartao).toContain(esperado);
    }
    expect(texto('a-cobrar')).toContain('20,00');
    expect(texto('a-receber')).toContain('170,00');
    expect(texto('saldo')).toContain('170,00');
  });

  it('US2.3: saldo negativo aparece como crédito da pessoa', async () => {
    const { texto } = await abrir([linha({ accumulatedCents: -4550, toChargeCents: 0 })]);
    expect(texto('saldo')).toContain('Crédito da Bruna: R$');
    expect(texto('saldo')).toContain('45,50');
  });

  it('US3.3: sem A cobrar o botão "Gerar cobrança" não aparece', async () => {
    const { el } = await abrir([linha({ toChargeCents: 0 })]);
    expect(el.querySelector('[data-testid="gerar-cobranca"]')).toBeNull();
  });

  it('sem tags de acerto mostra o estado vazio', async () => {
    const { texto } = await abrir([]);
    expect(texto('sem-acertos')).toContain('Nenhuma tag de acerto');
  });

  it('US3.1: "Gerar cobrança" abre o formulário pré-preenchido, sem salvar nada', async () => {
    const abrirPreenchida = vi
      .spyOn(TestBed.inject(LancamentoLauncher), 'abrirPreenchida')
      .mockReturnValue(undefined as never);
    const { el } = await abrir();
    el.querySelector<HTMLElement>('[data-testid="gerar-cobranca"]')!.click();
    expect(abrirPreenchida).toHaveBeenCalledOnce();
    const dados = abrirPreenchida.mock.calls[0][0];
    expect(dados).toMatchObject({
      description: 'Bruna — acerto 03/2026',
      amountCents: 2000,
      referenceMonth: '2026-03',
      tagIds: ['t-bruna'],
    });
    // hoje fora de março/2026 → dia 1 do mês (US3.2)
    expect(dados.date).toBe(today().slice(0, 7) === '2026-03' ? today() : '2026-03-01');
  });

  it('salvar a cobrança recarrega os números (A cobrar → 0,00)', async () => {
    const { fixture, texto } = await abrir();
    TestBed.inject(EntriesService)
      .create(
        { kind: 'income', description: 'Bruna', amount: '20', date: '2026-03-20', walletId: 'w' },
        'k',
      )
      .subscribe();
    http.expectOne('/api/v1/entries').flush({ id: 'e1' });
    http.expectOne('/api/v1/settlements/2026-03').flush([linha({ toChargeCents: 0 })]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(texto('a-cobrar')).toContain('0,00');
  });

  it('tocar em Gasto lista os lançamentos que o compõem; Recebido só os já recebidos', async () => {
    const itens: SettlementItem[] = [
      {
        group: 'spent',
        type: 'card_purchase',
        id: '1',
        description: 'Ifood',
        date: '2026-03-05',
        signedCents: 15681,
      },
      {
        group: 'spent',
        type: 'card_purchase',
        id: '2',
        description: 'Estorno',
        date: '2026-03-06',
        signedCents: -953,
      },
      {
        group: 'charged',
        type: 'income',
        id: '3',
        description: 'Bruna',
        date: '2026-03-07',
        signedCents: 15000,
        received: false,
      },
      {
        group: 'charged',
        type: 'income',
        id: '4',
        description: 'Bruna Pix',
        date: '2026-03-08',
        signedCents: 3000,
        received: true,
      },
    ];
    const { fixture, el } = await abrir();
    const descricoes = () =>
      Array.from(el.querySelectorAll('[data-testid="item"] .descricao')).map((e) => e.textContent);

    el.querySelector<HTMLElement>('[data-testid="numero-spent"]')!.click();
    http.expectOne('/api/v1/settlements/2026-03/tags/t-bruna/items').flush(itens);
    fixture.detectChanges();
    expect(descricoes()).toEqual(['Ifood', 'Estorno']);
    expect(el.querySelector('[data-testid="item"]')!.textContent).toContain('156,81');

    el.querySelector<HTMLElement>('[data-testid="numero-received"]')!.click();
    fixture.detectChanges();
    expect(descricoes()).toEqual(['Bruna Pix']);

    el.querySelector<HTMLElement>('[data-testid="numero-charged"]')!.click();
    fixture.detectChanges();
    expect(descricoes()).toEqual(['Bruna', 'Bruna Pix']);

    // tocar de novo no mesmo número fecha a lista
    el.querySelector<HTMLElement>('[data-testid="numero-charged"]')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="detalhe"]')).toBeNull();
  });

  it('as setas trocam de mês', async () => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { el } = await abrir();
    el.querySelector<HTMLElement>('[data-testid="acertos-seguinte"]')!.click();
    expect(navegar).toHaveBeenCalledWith(['/acertos', '2026-04']);
    el.querySelector<HTMLElement>('[data-testid="acertos-anterior"]')!.click();
    expect(navegar).toHaveBeenCalledWith(['/acertos', '2026-02']);
  });
});
