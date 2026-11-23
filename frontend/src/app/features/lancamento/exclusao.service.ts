import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { problemMessage } from '../../core/errors/problem-details';
import { DeletionResult } from './entry.models';

/** Tempo do "Desfazer" depois de excluir (RT-09). */
export const DESFAZER_MS = 8000;

export type TipoExcluivel = 'entry' | 'transfer';

/**
 * Exclusão com "Desfazer" (RT-09, research R3) para entradas, saídas e transferências:
 * `DELETE` devolve o lote; o snackbar "Excluído — Desfazer" dura 8 s e desfaz por
 * `POST /deletions/{batchId}/restore`.
 */
@Injectable({ providedIn: 'root' })
export class ExclusaoService {
  private readonly api = inject(ApiClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly mudouSubject = new Subject<void>();

  /** Emite depois de excluir ou desfazer, para as telas recarregarem (FR-016). */
  readonly mudou$ = this.mudouSubject.asObservable();

  excluir(tipo: TipoExcluivel, id: string): Observable<DeletionResult> {
    const rota = tipo === 'entry' ? 'entries' : 'transfers';
    return this.api
      .delete<DeletionResult>(`/${rota}/${id}`)
      .pipe(tap(() => this.mudouSubject.next()));
  }

  restaurar(batchId: string): Observable<void> {
    return this.api
      .post<void>(`/deletions/${batchId}/restore`)
      .pipe(tap(() => this.mudouSubject.next()));
  }

  /** Mostra "Excluído — Desfazer" por 8 s; o desfazer avisa o resultado num segundo aviso. */
  oferecerDesfazer(batchId: string): void {
    const ref = this.snackBar.open('Excluído', 'Desfazer', {
      duration: DESFAZER_MS,
      verticalPosition: 'top',
    });
    ref.onAction().subscribe(() =>
      this.restaurar(batchId).subscribe({
        next: () =>
          this.snackBar.open('Lançamento restaurado.', undefined, {
            duration: 4000,
            verticalPosition: 'top',
          }),
        error: (error: unknown) =>
          this.snackBar.open(problemMessage(error), 'Fechar', {
            duration: DESFAZER_MS,
            verticalPosition: 'top',
          }),
      }),
    );
  }
}
