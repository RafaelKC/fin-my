import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { bruna, carla, ifood } from '../../shared/tag-picker/testing';
import { carteira } from '../carteiras/testing';
import { Entry } from './entry.models';
import { EntradaPreenchida, LancamentoSheet } from './lancamento.sheet';

// 004 T007 — "Gerar cobrança": a Entrada vem pré-preenchida e nada é salvo sem "Salvar" (US3.1, US3.4).
describe('LancamentoSheet — Entrada pré-preenchida (Gerar cobrança)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;

  const cobranca: EntradaPreenchida = {
    description: 'Bruna — acerto 03/2026',
    amountCents: 2000,
    date: '2026-03-20',
    referenceMonth: '2026-03',
    tagIds: ['t-bruna'],
  };

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function abrir(dados: EntradaPreenchida = cobranca) {
    localStorage.clear();
    // última Entrada foi numa carteira que NÃO é a principal; o rascunho de outro lançamento existe
    localStorage.setItem(
      'finmy.prefs.v1',
      JSON.stringify({ lastKind: 'expense', lastWalletByKind: { income: 'w-verde' } }),
    );
    localStorage.setItem(
      'finmy.draft.lancamento',
      JSON.stringify({
        savedOn: '2026-03-20',
        kind: 'expense',
        amount: '9',
        description: 'Rascunho',
      }),
    );
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { preencher: dados } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http
      .match((r) => r.url.startsWith('/api/v1/wallets'))
      .forEach((r) =>
        r.flush([
          carteira({ id: 'w-verde', name: 'Verde', isPrimary: false }),
          carteira({ id: 'w-azul', name: 'Azul', isPrimary: true }),
        ]),
      );
    http.match('/api/v1/tags?archived=all').forEach((r) => r.flush([bruna, carla, ifood]));
    await atualizar();
  }
  const campo = (seletor: string) => el.querySelector<HTMLInputElement>(seletor)!;

  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    http.verify();
    localStorage.clear();
  });

  it('abre como Entrada Prevista com descrição, valor, data, carteira principal e tag (US3.1)', async () => {
    await abrir();
    expect(campo('#lancamento-descricao').value).toBe('Bruna — acerto 03/2026');
    expect(campo('#amount-field').value).toContain('20,00');
    expect(campo('#lancamento-data').value).toBe('2026-03-20');
    expect(campo('#lancamento-carteira').value).toBe('w-azul');
    expect(el.querySelector('[data-testid="tag-chip"][data-tag="Bruna"]')).not.toBeNull();
    // não carrega o rascunho de outro lançamento
    expect(campo('#lancamento-descricao').value).not.toBe('Rascunho');
  });

  it('salvar envia a Entrada Prevista com a tag e sem mexer no rascunho (US3.1)', async () => {
    await abrir();
    el.querySelector<HTMLElement>('[data-testid="salvar"]')!.click();
    await atualizar();
    const req = http.expectOne('/api/v1/entries');
    expect(req.request.body).toEqual({
      kind: 'income',
      description: 'Bruna — acerto 03/2026',
      amount: '20,00',
      date: '2026-03-20',
      walletId: 'w-azul',
      status: 'planned',
      tagIds: ['t-bruna'],
    });
    req.flush({
      id: 'e1',
      kind: 'income',
      description: 'Bruna — acerto 03/2026',
      amountCents: 2000,
      date: '2026-03-20',
      referenceMonth: '2026-03',
      status: 'planned',
      walletId: 'w-azul',
      createdAt: '2026-03-20T12:00:00Z',
      version: 1,
    } satisfies Entry);
  });

  it('mês de referência diferente do da data vai no corpo (cobrança de mês passado)', async () => {
    await abrir({ ...cobranca, date: '2026-04-20', referenceMonth: '2026-03' });
    el.querySelector<HTMLElement>('[data-testid="salvar"]')!.click();
    await atualizar();
    const req = http.expectOne('/api/v1/entries');
    expect(req.request.body).toMatchObject({ date: '2026-04-20', referenceMonth: '2026-03' });
    req.flush({}, { status: 500, statusText: 'x' });
  });

  it('o que a pessoa digitar antes de salvar prevalece (US3.4)', async () => {
    await abrir();
    const valor = campo('#amount-field');
    valor.value = '25,50';
    valor.dispatchEvent(new Event('input'));
    await atualizar();
    el.querySelector<HTMLElement>('[data-testid="salvar"]')!.click();
    await atualizar();
    const req = http.expectOne('/api/v1/entries');
    expect(req.request.body).toMatchObject({ amount: '25,50' });
    req.flush({}, { status: 500, statusText: 'x' });
  });

  it('não salva nada ao abrir', async () => {
    await abrir();
    http.expectNone('/api/v1/entries');
  });
});
