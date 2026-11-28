import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { LancamentoLauncher } from '../lancamento/lancamento.launcher';
import { EntradaPreenchida } from '../lancamento/lancamento.sheet';
import { SugestoesPage } from './sugestoes.page';
import { Sugestao } from './sugestoes.service';

const SUGESTAO: Sugestao = {
  id: 's1',
  source: 'PicPay',
  bank: '380',
  kind: 'card_purchase',
  description: 'Padaria X',
  amountCents: 4590,
  amountText: 'R$ 45,90',
  occurredAt: '2026-03-20T14:05:00Z',
  cardLast4: '1234',
  status: 'pending',
  suggestedCreditCardId: 'c-picpay',
  suggestedWalletId: null,
};

describe('SugestoesPage (010)', () => {
  let http: HttpTestingController;
  let abertas: EntradaPreenchida[];

  function abrir(id: string | null = null) {
    abertas = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
        {
          provide: LancamentoLauncher,
          useValue: { abrirPreenchida: (p: EntradaPreenchida) => abertas.push(p) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SugestoesPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/suggestions').flush([SUGESTAO]);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => http.verify());

  it('lista as pendentes com valor, origem e final do cartão', () => {
    const el = abrir();
    const item = el.querySelector('[data-testid="sugestao"]')!;
    expect(item.textContent).toContain('Padaria X');
    expect(item.textContent).toContain('R$ 45,90');
    expect(item.textContent).toContain('PicPay');
    expect(item.textContent).toContain('final 1234');
  });

  it('Lançar abre o formulário pré-preenchido no cartão do banco; salvar marca como lançada', () => {
    const el = abrir();
    Array.from(el.querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim() === 'Lançar')!
      .click();

    expect(abertas).toHaveLength(1);
    expect(abertas[0]).toEqual(
      expect.objectContaining({
        kind: 'card',
        description: 'Padaria X',
        amountCents: 4590,
        cardId: 'c-picpay',
      }),
    );
    abertas[0].aoSalvar!(null);
    const req = http.expectOne('/api/v1/suggestions/s1/added');
    expect(req.request.body).toEqual({ linkedId: null });
    req.flush(null);
  });

  it('Descartar tira da lista', () => {
    const el = abrir();
    Array.from(el.querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim() === 'Descartar')!
      .click();
    http.expectOne('/api/v1/suggestions/s1/dismiss').flush(null);
  });

  it('/sugestoes/:id (toque na notificação) abre direto o formulário daquela sugestão', () => {
    abrir('s1');
    expect(abertas.map((p) => p.description)).toEqual(['Padaria X']);
  });
});
