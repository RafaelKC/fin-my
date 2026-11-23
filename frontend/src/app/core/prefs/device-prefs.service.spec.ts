import { TestBed } from '@angular/core/testing';
import { DevicePrefsService } from './device-prefs.service';

describe('DevicePrefsService', () => {
  let prefs: DevicePrefsService;

  beforeEach(() => {
    localStorage.clear();
    prefs = TestBed.inject(DevicePrefsService);
  });
  afterEach(() => vi.restoreAllMocks());

  it('primeiro uso: tipo Saída e nenhuma carteira lembrada (FR-009)', () => {
    expect(prefs.lastKind()).toBe('expense');
    expect(prefs.lastWalletId('expense')).toBeNull();
  });

  it('lembra o último tipo e a última carteira por tipo (FR-010)', () => {
    prefs.rememberSaved('expense', 'w-azul');
    prefs.rememberSaved('income', 'w-verde');
    expect(prefs.lastKind()).toBe('income');
    expect(prefs.lastWalletId('expense')).toBe('w-azul');
    expect(prefs.lastWalletId('income')).toBe('w-verde');
    expect(JSON.parse(localStorage.getItem('finmy.prefs.v1')!)).toEqual({
      lastKind: 'income',
      lastWalletByKind: { expense: 'w-azul', income: 'w-verde' },
    });
  });

  it('ignora preferências corrompidas', () => {
    localStorage.setItem('finmy.prefs.v1', '{não é json');
    expect(prefs.lastKind()).toBe('expense');
    localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: 'cartao' }));
    expect(prefs.lastKind()).toBe('expense');
  });

  it('lembra Transferência como último tipo e a origem usada (FR-009)', () => {
    prefs.rememberSaved('transfer', 'w-azul');
    expect(prefs.lastKind()).toBe('transfer');
    expect(prefs.lastWalletId('transfer')).toBe('w-azul');
  });

  it('guarda e apaga o rascunho', () => {
    expect(prefs.readDraft()).toBeNull();
    prefs.saveDraft({ amount: '10' });
    expect(prefs.readDraft()).toEqual({ amount: '10' });
    prefs.clearDraft();
    expect(prefs.readDraft()).toBeNull();
  });

  it('com o armazenamento bloqueado, nada quebra', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bloqueado');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('bloqueado');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('bloqueado');
    });
    expect(prefs.lastKind()).toBe('expense');
    expect(() => prefs.rememberSaved('income', 'w')).not.toThrow();
    expect(() => prefs.saveDraft({})).not.toThrow();
    expect(() => prefs.clearDraft()).not.toThrow();
    expect(prefs.readDraft()).toBeNull();
  });
});
