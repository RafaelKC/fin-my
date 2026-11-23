import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../../core/api/api-client';
import { Entry } from '../../lancamento/entry.models';
import { AcaoPendencia, ResultadoLote } from './pendencias.models';

/** Pendências (FR-019/FR-020): `GET /entries/pending`, `POST /entries/bulk` e o desfazer da exclusão. */
@Injectable({ providedIn: 'root' })
export class PendenciasService {
  private readonly api = inject(ApiClient);
  private readonly alteradasSubject = new Subject<void>();

  /** Emite depois de qualquer ação em lote ou desfazer, para a visão do mês recarregar (FR-016). */
  readonly alteradas$ = this.alteradasSubject.asObservable();

  list(): Observable<Entry[]> {
    return this.api.get<Entry[]>('/entries/pending');
  }

  bulk(ids: readonly string[], action: AcaoPendencia): Observable<ResultadoLote> {
    return this.api
      .post<ResultadoLote>('/entries/bulk', { ids, action })
      .pipe(tap(() => this.alteradasSubject.next()));
  }

  /** Desfaz uma exclusão (RT-09): `POST /deletions/{batchId}/restore`. */
  restaurar(batchId: string): Observable<void> {
    return this.api
      .post<void>(`/deletions/${batchId}/restore`)
      .pipe(tap(() => this.alteradasSubject.next()));
  }
}
