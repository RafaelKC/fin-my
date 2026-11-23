import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { Entry, EntryCreate, EntryKind, EntrySuggestion } from './entry.models';

/** Acesso à API de entradas e saídas (`/api/v1/entries`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class EntriesService {
  private readonly api = inject(ApiClient);
  private readonly criadosSubject = new Subject<Entry>();

  /** Emite cada lançamento criado, para as telas (ex.: visão do mês, FR-016) recarregarem. */
  readonly criados$ = this.criadosSubject.asObservable();

  /** `idempotencyKey`: uma por formulário, reaproveitada em reenvios (research R2, R12). */
  create(input: EntryCreate, idempotencyKey: string): Observable<Entry> {
    return this.api
      .post<Entry>('/entries', input, { 'Idempotency-Key': idempotencyKey })
      .pipe(tap((entry) => this.criadosSubject.next(entry)));
  }

  suggestions(q: string, kind: EntryKind): Observable<EntrySuggestion[]> {
    return this.api.get<EntrySuggestion[]>(
      `/entries/suggestions?q=${encodeURIComponent(q)}&kind=${kind}`,
    );
  }
}
