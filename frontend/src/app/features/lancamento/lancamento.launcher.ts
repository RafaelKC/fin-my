import { Injectable, inject } from '@angular/core';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Entry } from './entry.models';
import { LancamentoSheet } from './lancamento.sheet';

/** Largura a partir da qual o formulário é diálogo (abaixo disso, tela cheia). */
const DESKTOP = '(min-width: 1024px)';

/** Abre o formulário rápido: bottom sheet em tela cheia no celular, diálogo no computador. */
@Injectable({ providedIn: 'root' })
export class LancamentoLauncher {
  private readonly dialog = inject(MatDialog);
  private aberto: MatDialogRef<LancamentoSheet, Entry | undefined> | null = null;

  abrir(): MatDialogRef<LancamentoSheet, Entry | undefined> {
    if (this.aberto !== null) return this.aberto;

    const desktop = typeof matchMedia === 'function' && matchMedia(DESKTOP).matches;
    const ref = this.dialog.open<LancamentoSheet, undefined, Entry | undefined>(
      LancamentoSheet,
      desktop
        ? { width: '32rem', maxWidth: '95vw', maxHeight: '95vh', autoFocus: false }
        : {
            width: '100vw',
            maxWidth: '100vw',
            height: '100dvh',
            maxHeight: '100dvh',
            panelClass: 'lancamento-tela-cheia',
            autoFocus: false,
          },
    );
    this.aberto = ref;
    ref.afterClosed().subscribe(() => (this.aberto = null));
    return ref;
  }
}
