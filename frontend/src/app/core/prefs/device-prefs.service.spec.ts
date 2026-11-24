import { TestBed } from '@angular/core/testing';
import { DevicePrefsService } from './device-prefs.service';

describe('DevicePrefsService', () => {
  let prefs: DevicePrefsService;

  beforeEach(() => {
    localStorage.clear();
    prefs = TestBed.inject(DevicePrefsService);
  });
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('primeiro uso: tipo Cartão e nenhuma carteira ou cartão lembrado (FR-009, 003 FR-010)', () => {
    expect(prefs.lastKind()).toBe('card');
    expect(prefs.lastWalletId('expense')).toBeNull();
    expect(prefs.lastCardId()).toBeNull();
  });

  it('lembra o último cartão usado e o tipo Cartão (research R9)', () => {
    prefs.rememberSaved('expense', 'w-azul');
    prefs.rememberSaved('card', 'c-azul');
    expect(prefs.lastKind()).toBe('card');
    expect(prefs.lastCardId()).toBe('c-azul');
    expect(prefs.lastWalletId('expense')).toBe('w-azul');
    prefs.rememberSaved('income', 'w-verde');
    expect(prefs.lastKind()).toBe('income');
    expect(prefs.lastCardId()).toBe('c-azul');
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
    expect(prefs.lastKind()).toBe('card');
    localStorage.setItem('finmy.prefs.v1', JSON.stringify({ lastKind: 'cartao' }));
    expect(prefs.lastKind()).toBe('card');
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

  it('tela inicial: Mês por padrão; a escolha do Painel é lembrada sem apagar o resto (FR-006)', () => {
    expect(prefs.homeScreen()).toBe('mes');
    prefs.rememberSaved('expense', 'w-azul');
    prefs.setHomeScreen('painel');
    expect(prefs.homeScreen()).toBe('painel');
    expect(prefs.lastWalletId('expense')).toBe('w-azul');
    prefs.setHomeScreen('mes');
    expect(prefs.homeScreen()).toBe('mes');
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
    expect(prefs.lastKind()).toBe('card');
    expect(() => prefs.rememberSaved('income', 'w')).not.toThrow();
    expect(prefs.homeScreen()).toBe('mes');
    expect(() => prefs.setHomeScreen('painel')).not.toThrow();
    expect(() => prefs.saveDraft({})).not.toThrow();
    expect(() => prefs.clearDraft()).not.toThrow();
    expect(prefs.readDraft()).toBeNull();
  });
});
