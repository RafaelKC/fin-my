import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { today } from '../../core/money';
import { DevicePrefsService } from '../../core/prefs/device-prefs.service';
import { carteira } from '../carteiras/testing';
import { LancamentoSheet } from './lancamento.sheet';

const azul = carteira();
const verde = carteira({ id: 'w-verde', name: 'Verde', isPrimary: false });
const transferenciaCriada = {
  id: 't1',
  fromWalletId: 'w-azul',
  toWalletId: 'w-verde',
  amountCents: 2000,
  date: today(),
  description: 'Transferência',
  createdAt: '',
  version: 1,
};

describe('LancamentoSheet — Transferência (US5)', () => {
  let fixture: ComponentFixture<LancamentoSheet>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;

  async function abrir(carteiras = [azul, verde]) {
    fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: fechar } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
    fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=false').flush(carteiras);
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
  async function escolher(seletor: string, valor: string) {
    const campo = el.querySelector<HTMLSelectElement>(seletor)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('change'));
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
  const tem = (seletor: string) => el.querySelector(seletor) !== null;

  // Primeiro uso abre em Cartão (003 FR-010); estes testes partem de Saída.
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: 'expense' }));
  });
  afterEach(() => {
    fixture.destroy();
    http.match((r) => r.url.startsWith('/api/v1/entries/suggestions'));
    try {
      http.verify();
    } finally {
      localStorage.clear();
    }
  });

  it('o tipo Transferência mostra De/Para e esconde situação e sugestões', async () => {
    await abrir();
    expect(tem('#lancamento-situacao')).toBe(true);
    radio('Transferência').click();
    await atualizar();

    expect(radio('Transferência').getAttribute('aria-checked')).toBe('true');
    expect(el.querySelector('label[for="lancamento-carteira"]')?.textContent).toBe('De');
    expect(el.querySelector('label[for="lancamento-destino"]')?.textContent).toBe('Para');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-carteira')!.value).toBe('w-azul');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-destino')!.value).toBe('w-verde');
    expect(tem('#lancamento-situacao')).toBe(false);
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.placeholder).toBe(
      'Transferência',
    );

    await clicar('.mais');
    expect(tem('#lancamento-mes')).toBe(false);
    expect(tem('#lancamento-obs')).toBe(true);
  });

  it('não pede sugestões ao digitar a descrição de uma transferência', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await digitar('#lancamento-descricao', 'Poup');
    await new Promise((r) => setTimeout(r, 200));
    http.expectNone((r) => r.url.startsWith('/api/v1/entries/suggestions'));
  });

  it('salva por POST /transfers sem descrição (padrão do servidor), com Idempotency-Key (US5.1)', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await digitar('#amount-field', '20');
    await clicar('[data-testid="salvar"]');

    const req = http.expectOne('/api/v1/transfers');
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(req.request.body).toEqual({
      fromWalletId: 'w-azul',
      toWalletId: 'w-verde',
      amount: '20',
      date: today(),
    });
    req.flush(transferenciaCriada);
    await atualizar();

    expect(fechar).toHaveBeenCalled();
    const prefs = TestBed.inject(DevicePrefsService);
    expect(prefs.lastKind()).toBe('transfer');
    expect(prefs.lastWalletId('transfer')).toBe('w-azul');
    expect(prefs.readDraft()).toBeNull();
  });

  it('envia a descrição e a observação digitadas', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await digitar('#amount-field', '10+10');
    await digitar('#lancamento-descricao', ' Poupança ');
    await clicar('.mais');
    await digitar('#lancamento-obs', ' reserva ');
    await escolher('#lancamento-carteira', 'w-verde');
    await escolher('#lancamento-destino', 'w-azul');
    await clicar('[data-testid="salvar"]');

    const req = http.expectOne('/api/v1/transfers');
    expect(req.request.body).toMatchObject({
      fromWalletId: 'w-verde',
      toWalletId: 'w-azul',
      description: 'Poupança',
      notes: 'reserva',
    });
    req.flush(transferenciaCriada);
  });

  it('origem e destino iguais: impede salvar e não chama a API (US5.3)', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await digitar('#amount-field', '20');
    await escolher('#lancamento-destino', 'w-azul');
    await clicar('[data-testid="salvar"]');

    expect(el.querySelector('[data-testid="erro-destino"]')?.textContent).toContain(
      'Escolha carteiras diferentes.',
    );
    http.expectNone('/api/v1/transfers');
  });

  it('trocar a origem para a carteira do destino reescolhe o destino automaticamente', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await escolher('#lancamento-carteira', 'w-verde');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-destino')!.value).toBe('w-azul');
  });

  it('com uma só carteira o destino fica por escolher', async () => {
    await abrir([azul]);
    radio('Transferência').click();
    await atualizar();
    await clicar('[data-testid="salvar"]');
    expect(el.querySelector('[data-testid="erro-destino"]')?.textContent).toContain(
      'Escolha a carteira de destino.',
    );
    http.expectNone('/api/v1/transfers');
  });

  it('"Salvar e lançar outro" mantém o tipo e limpa valor e descrição', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await digitar('#amount-field', '20');
    await digitar('#lancamento-descricao', 'Poupança');
    await clicar('[data-testid="salvar-outro"]');
    http.expectOne('/api/v1/transfers').flush(transferenciaCriada);
    await atualizar();

    expect(fechar).not.toHaveBeenCalled();
    expect(radio('Transferência').getAttribute('aria-checked')).toBe('true');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('');
  });

  it('erro do servidor transfer.same_wallet vira a mensagem do contrato', async () => {
    await abrir();
    radio('Transferência').click();
    await atualizar();
    await digitar('#amount-field', '20');
    await clicar('[data-testid="salvar"]');
    http
      .expectOne('/api/v1/transfers')
      .flush(
        { code: 'transfer.same_wallet', title: 'x', status: 422 },
        { status: 422, statusText: 'Unprocessable' },
      );
    await atualizar();
    expect(el.querySelector('[data-testid="erro-lancamento"]')?.textContent).toContain(
      'Escolha carteiras diferentes.',
    );
  });

  it('abre em Transferência quando foi o último tipo salvo no aparelho', async () => {
    localStorage.setItem(
      'finmy.prefs.v1',
      JSON.stringify({ lastKind: 'transfer', lastWalletByKind: { transfer: 'w-verde' } }),
    );
    await abrir();
    expect(radio('Transferência').getAttribute('aria-checked')).toBe('true');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-carteira')!.value).toBe('w-verde');
    expect(el.querySelector<HTMLSelectElement>('#lancamento-destino')!.value).toBe('w-azul');
  });
});
