import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { today } from '../../core/money';
import { DevicePrefsService } from '../../core/prefs/device-prefs.service';
import { carteira } from '../carteiras/testing';
import { Entry } from './entry.models';
import { LancamentoSheet, MENSAGEM_SEM_CONEXAO_LANCAMENTO } from './lancamento.sheet';
import { DialogoRef } from '@/shared/ui/dialogo';
import { Avisos } from '@/shared/ui/avisos';

const azul = carteira();
const verde = carteira({ id: 'w-verde', name: 'Verde', isPrimary: false });

function entrada(parcial: Partial<Entry> = {}): Entry {
  return {
    id: 'e1',
    kind: 'expense',
    description: 'Tim',
    amountCents: 4499,
    date: today(),
    referenceMonth: today().slice(0, 7),
    status: 'settled',
    walletId: 'w-azul',
    createdAt: '2026-03-10T15:00:00Z',
    version: 1,
    ...parcial,
  };
}

describe('LancamentoSheet', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;
  let aviso: ReturnType<typeof vi.spyOn>;

  async function abrir(carteiras = [azul, verde]) {
    fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DialogoRef, useValue: { close: fechar } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    aviso = vi.spyOn(TestBed.inject(Avisos), 'open').mockReturnValue(undefined as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=false').flush(carteiras);
    http.match('/api/v1/tags?archived=all').forEach((req) => req.flush([]));
    await atualizar();
  }
  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function digitar(seletor: string, valor: string) {
    const campo = el.querySelector<HTMLInputElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  }
  const clicar = async (seletor: string) => {
    el.querySelector<HTMLElement>(seletor)!.click();
    await atualizar();
  };
  const radio = (nome: string) =>
    Array.from(el.querySelectorAll<HTMLElement>('[role="radio"]')).find(
      (b) => b.textContent?.trim() === nome,
    )!;
  const selecionada = (nome: string) => radio(nome).getAttribute('aria-checked') === 'true';
  const carteiraEscolhida = () =>
    el.querySelector<HTMLSelectElement>('#lancamento-carteira')!.value;
  const pausa = (ms: number) => new Promise((r) => setTimeout(r, ms));
  const criacao = (): TestRequest => http.expectOne('/api/v1/entries');

  async function preencher(valor = '44,99', descricao = 'Tim') {
    await digitar('#amount-field', valor);
    await digitar('#lancamento-descricao', descricao);
  }

  // Primeiro uso abre em Cartão (003 FR-010); estes testes partem de Saída.
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: 'expense' }));
  });
  afterEach(() => {
    fixture.destroy();
    // sugestões pendentes de quem digitou a descrição não são o foco destes testes
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    try {
      http.verify();
    } finally {
      localStorage.clear();
    }
  });

  it('abre com Saída, hoje, carteira principal e situação pela data (FR-009, FR-010, FR-012)', async () => {
    await abrir();
    expect(selecionada('Saída')).toBe(true);
    expect(el.querySelector<HTMLInputElement>('#lancamento-data')!.value).toBe(today());
    expect(carteiraEscolhida()).toBe('w-azul');
    expect(selecionada('Paga')).toBe(true);
  });

  it('usa o último tipo e a última carteira do tipo, se ainda ativa', async () => {
    localStorage.setItem(
      'finmy.prefs.v1',
      JSON.stringify({ lastKind: 'income', lastWalletByKind: { income: 'w-verde' } }),
    );
    await abrir();
    expect(selecionada('Entrada')).toBe(true);
    expect(carteiraEscolhida()).toBe('w-verde');
    expect(selecionada('Recebida')).toBe(true);
  });

  it('carteira lembrada que não está mais ativa cai na principal', async () => {
    localStorage.setItem(
      'finmy.prefs.v1',
      JSON.stringify({ lastKind: 'expense', lastWalletByKind: { expense: 'w-antiga' } }),
    );
    await abrir();
    expect(carteiraEscolhida()).toBe('w-azul');
  });

  it('a situação segue a data até ser alterada à mão (FR-012)', async () => {
    await abrir();
    await digitar('#lancamento-data', '2999-01-01');
    expect(selecionada('Prevista')).toBe(true);
    await digitar('#lancamento-data', today());
    expect(selecionada('Paga')).toBe(true);

    radio('Prevista').click();
    await digitar('#lancamento-data', '2020-01-01');
    expect(selecionada('Prevista')).toBe(true);
  });

  it('valor 0 ou -5 não salva e não chama a API (US2.6)', async () => {
    await abrir();
    for (const texto of ['0', '-5']) {
      await preencher(texto);
      await clicar('[data-testid="salvar"]');
      expect(el.textContent).toContain('Informe um valor maior que zero');
    }
    http.expectNone('/api/v1/entries');
  });

  it('valor vazio e descrição vazia mostram os erros', async () => {
    await abrir();
    await clicar('[data-testid="salvar"]');
    expect(el.querySelector('[data-testid="erro-valor"]')?.textContent).toContain(
      'Informe o valor',
    );
    expect(el.querySelector('[data-testid="erro-descricao"]')?.textContent).toContain(
      'Informe a descrição',
    );
    http.expectNone('/api/v1/entries');
  });

  it('salva só o que a pessoa decidiu, com Idempotency-Key, e fecha (US2.1, US2.2)', async () => {
    await abrir();
    await preencher('29+73,2', '  Mercado ');
    await clicar('[data-testid="salvar"]');

    const req = criacao();
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(req.request.body).toEqual({
      kind: 'expense',
      description: 'Mercado',
      amount: '29+73,2',
      date: today(),
      walletId: 'w-azul',
    });
    req.flush(entrada());
    await atualizar();

    expect(fechar).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }));
    const prefs = TestBed.inject(DevicePrefsService);
    expect(prefs.lastKind()).toBe('expense');
    expect(prefs.lastWalletId('expense')).toBe('w-azul');
    expect(prefs.readDraft()).toBeNull();
  });

  it('envia situação e mês de referência só quando alterados à mão; observação se houver', async () => {
    await abrir();
    await preencher();
    radio('Prevista').click();
    await clicar('.mais');
    await digitar('#lancamento-mes', '2999-12');
    await digitar('#lancamento-obs', ' combinado ');
    await clicar('[data-testid="salvar"]');

    const corpo = criacao();
    expect(corpo.request.body).toMatchObject({
      status: 'planned',
      referenceMonth: '2999-12',
      notes: 'combinado',
    });
    corpo.flush(entrada());
  });

  it('sugestão aplica a carteira só se não foi alterada à mão, e nunca o valor (FR-011)', async () => {
    await abrir();
    await digitar('#lancamento-descricao', 'Ti');
    await pausa(160);
    const req = http.expectOne((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    expect(req.request.url).toBe('/api/v1/entries/suggestions?q=Ti&kind=expense');
    req.flush([{ description: 'Tim', uses: 3, walletId: 'w-verde' }]);
    await atualizar();

    await clicar('[data-testid="sugestao"]');
    expect(carteiraEscolhida()).toBe('w-verde');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Tim');
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('');
    expect(el.querySelector('[data-testid="sugestao"]')).toBeNull();
  });

  it('carteira escolhida à mão não é trocada pela sugestão', async () => {
    await abrir();
    const select = el.querySelector<HTMLSelectElement>('#lancamento-carteira')!;
    select.value = 'w-azul';
    select.dispatchEvent(new Event('change'));
    await digitar('#lancamento-descricao', 'Ti');
    await pausa(160);
    http
      .expectOne((r) => r.url.startsWith('/api/v1/entries/suggestions'))
      .flush([{ description: 'Tim', uses: 3, walletId: 'w-verde' }]);
    await atualizar();
    await clicar('[data-testid="sugestao"]');
    expect(carteiraEscolhida()).toBe('w-azul');
  });

  it('sem conexão: avisa, mantém o formulário e reenvia com a mesma chave (US2.7)', async () => {
    await abrir();
    await preencher();
    await clicar('[data-testid="salvar"]');
    const primeira = criacao();
    const chave = primeira.request.headers.get('Idempotency-Key');
    primeira.error(new ProgressEvent('error'), { status: 0 });
    await atualizar();

    expect(aviso).toHaveBeenCalledWith(
      MENSAGEM_SEM_CONEXAO_LANCAMENTO,
      'Fechar',
      expect.anything(),
    );
    expect(fechar).not.toHaveBeenCalled();
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('44,99');

    await clicar('[data-testid="salvar"]');
    const segunda = criacao();
    expect(segunda.request.headers.get('Idempotency-Key')).toBe(chave);
    segunda.flush(entrada());
  });

  it('navigator.onLine falso: não chama a API e avisa', async () => {
    await abrir();
    await preencher();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    try {
      await clicar('[data-testid="salvar"]');
    } finally {
      online.mockRestore();
    }
    http.expectNone('/api/v1/entries');
    expect(aviso).toHaveBeenCalledWith(
      MENSAGEM_SEM_CONEXAO_LANCAMENTO,
      'Fechar',
      expect.anything(),
    );
  });

  it('corpo alterado depois de uma falha usa outra chave', async () => {
    await abrir();
    await preencher();
    await clicar('[data-testid="salvar"]');
    const primeira = criacao();
    primeira.error(new ProgressEvent('error'), { status: 0 });
    await atualizar();

    await digitar('#amount-field', '45');
    await clicar('[data-testid="salvar"]');
    const segunda = criacao();
    expect(segunda.request.headers.get('Idempotency-Key')).not.toBe(
      primeira.request.headers.get('Idempotency-Key'),
    );
    segunda.flush(entrada());
  });

  it('erro da API aparece no formulário, que continua aberto', async () => {
    await abrir();
    await preencher();
    await clicar('[data-testid="salvar"]');
    criacao().flush(
      { code: 'validation_failed', detail: 'A descrição é inválida.' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await atualizar();
    expect(el.querySelector('[data-testid="erro-lancamento"]')?.textContent).toContain(
      'A descrição é inválida.',
    );
    expect(fechar).not.toHaveBeenCalled();
  });

  it('"Salvar e lançar outro" limpa, mantém tipo, data e carteira, e troca a chave (FR-013)', async () => {
    await abrir();
    radio('Entrada').click();
    await atualizar();
    const select = el.querySelector<HTMLSelectElement>('#lancamento-carteira')!;
    select.value = 'w-verde';
    select.dispatchEvent(new Event('change'));
    await digitar('#lancamento-data', '2020-02-02');
    await preencher('1500', 'Salário');
    await clicar('[data-testid="salvar-outro"]');
    const primeira = criacao();
    primeira.flush(entrada({ kind: 'income', walletId: 'w-verde' }));
    await atualizar();

    expect(fechar).not.toHaveBeenCalled();
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('');
    expect(selecionada('Entrada')).toBe(true);
    expect(carteiraEscolhida()).toBe('w-verde');
    expect(el.querySelector<HTMLInputElement>('#lancamento-data')!.value).toBe('2020-02-02');

    await preencher('20', 'Duda');
    await clicar('[data-testid="salvar"]');
    const segunda = criacao();
    expect(segunda.request.headers.get('Idempotency-Key')).not.toBe(
      primeira.request.headers.get('Idempotency-Key'),
    );
    segunda.flush(entrada({ kind: 'income', walletId: 'w-verde' }));
  });

  it('avisa quando a data é anterior ao saldo inicial da carteira, sem bloquear', async () => {
    await abrir();
    expect(el.querySelector('[data-testid="aviso-data-anterior"]')).toBeNull();
    await digitar('#lancamento-data', '2026-02-20');
    expect(el.querySelector('[data-testid="aviso-data-anterior"]')?.textContent).toContain(
      'anterior ao saldo inicial da carteira',
    );
    await preencher();
    await clicar('[data-testid="salvar"]');
    criacao().flush(entrada());
  });

  it('sem carteira: cria uma ali mesmo sem perder o que foi digitado', async () => {
    await abrir([]);
    await preencher('44,99', 'Tim');
    expect(el.querySelector('#lancamento-carteira')).toBeNull();
    await clicar('button.secundario');
    await digitar('#nova-carteira-nome', 'Azul');
    await digitar('#nova-carteira-saldo', '500');
    expect(el.querySelector('[data-testid="nova-carteira-previa"]')?.textContent).toContain(
      '= R$ 500,00',
    );
    const botoes = Array.from(el.querySelectorAll<HTMLButtonElement>('.mini button'));
    botoes[0].click();
    const req = http.expectOne('/api/v1/wallets');
    expect(req.request.body).toMatchObject({ name: 'Azul', initialBalance: '500' });
    req.flush(carteira({ id: 'w-nova', name: 'Azul' }));
    await atualizar();

    expect(carteiraEscolhida()).toBe('w-nova');
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('44,99');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Tim');
  });

  it('guarda rascunho e o restaura ao reabrir; salvar o apaga', async () => {
    await abrir();
    await preencher('30', 'Léo Casa');
    expect(TestBed.inject(DevicePrefsService).readDraft()).toMatchObject({
      amount: '30',
      description: 'Léo Casa',
    });
    fixture.destroy();
    TestBed.resetTestingModule();

    await abrir();
    expect(el.querySelector<HTMLInputElement>('#amount-field')!.value).toBe('30');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Léo Casa');
    await clicar('[data-testid="salvar"]');
    criacao().flush(entrada());
    await atualizar();
    expect(TestBed.inject(DevicePrefsService).readDraft()).toBeNull();
  });

  it('rascunho de outro dia não restaura a data', async () => {
    localStorage.setItem(
      'finmy.draft.lancamento',
      JSON.stringify({
        savedOn: '2020-01-01',
        kind: 'expense',
        amount: '5',
        description: 'Velho',
        date: '2020-01-01',
        walletId: 'w-azul',
        walletManual: false,
        status: null,
        refMonth: null,
        notes: '',
      }),
    );
    await abrir();
    expect(el.querySelector<HTMLInputElement>('#lancamento-data')!.value).toBe(today());
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Velho');
  });
});
