import { Injectable } from '@angular/core';

/** Tipos que o formulário rápido grava por aparelho (Transferência entra na T016). */
export type LancamentoKind = 'expense' | 'income';

const PREFS_KEY = 'finmy.prefs.v1';
const DRAFT_KEY = 'finmy.draft.lancamento';

interface Prefs {
  lastKind?: LancamentoKind;
  lastWalletByKind?: Partial<Record<LancamentoKind, string>>;
}

function isKind(value: unknown): value is LancamentoKind {
  return value === 'expense' || value === 'income';
}

/**
 * Preferências e rascunhos do aparelho em `localStorage` (research R12, arquitetura §9): último
 * tipo e última carteira por tipo (FR-009, FR-010) e o rascunho do formulário rápido. Nada aqui
 * vai ao servidor; se o armazenamento estiver bloqueado, tudo vira no-op.
 */
@Injectable({ providedIn: 'root' })
export class DevicePrefsService {
  /** Último tipo salvo neste aparelho; no primeiro uso, Saída. */
  lastKind(): LancamentoKind {
    const kind = this.readPrefs().lastKind;
    return isKind(kind) ? kind : 'expense';
  }

  /** Última carteira usada no tipo, ou `null` se nunca houve uso. */
  lastWalletId(kind: LancamentoKind): string | null {
    const id = this.readPrefs().lastWalletByKind?.[kind];
    return typeof id === 'string' && id !== '' ? id : null;
  }

  /** Registra o tipo e a carteira de um lançamento salvo. */
  rememberSaved(kind: LancamentoKind, walletId: string): void {
    const prefs = this.readPrefs();
    this.write(PREFS_KEY, {
      ...prefs,
      lastKind: kind,
      lastWalletByKind: { ...prefs.lastWalletByKind, [kind]: walletId },
    });
  }

  readDraft<T>(): T | null {
    return this.read<T>(DRAFT_KEY);
  }

  saveDraft(draft: unknown): void {
    this.write(DRAFT_KEY, draft);
  }

  clearDraft(): void {
    try {
      localStorage.removeItem(DRAFT_KEY);
    } catch {
      // armazenamento indisponível: sem rascunho
    }
  }

  private readPrefs(): Prefs {
    const prefs = this.read<Prefs>(PREFS_KEY);
    return prefs !== null && typeof prefs === 'object' ? prefs : {};
  }

  private read<T>(key: string): T | null {
    try {
      const raw = localStorage.getItem(key);
      return raw === null ? null : (JSON.parse(raw) as T);
    } catch {
      return null;
    }
  }

  private write(key: string, value: unknown): void {
    try {
      localStorage.setItem(key, JSON.stringify(value));
    } catch {
      // armazenamento cheio ou bloqueado: segue sem persistir
    }
  }
}
