import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import {
  Entry,
  EntryCreate,
  EntryKind,
  EntryStatus,
  EntrySuggestion,
  EntryUpdate,
} from './entry.models';

/** Acesso à API de entradas e saídas (`/api/v1/entries`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class EntriesService {
  private readonly api = inject(ApiClient);
  private readonly criadosSubject = new Subject<Entry>();

  /** Emite cada lançamento criado, para as telas (ex.: visão do mês, FR-016) recarregarem. */
  readonly criados$ = this.criadosSubject.asObservable();

  private readonly alteradosSubject = new Subject<Entry>();

  /** Emite cada lançamento editado ou com a situação trocada (FR-016). */
  readonly alterados$ = this.alteradosSubject.asObservable();

  /** `idempotencyKey`: uma por formulário, reaproveitada em reenvios (research R2, R12). */
  create(input: EntryCreate, idempotencyKey: string): Observable<Entry> {
    return this.api
      .post<Entry>('/entries', input, { 'Idempotency-Key': idempotencyKey })
      .pipe(tap((entry) => this.criadosSubject.next(entry)));
  }

  /** Troca a situação com um toque (FR-006): `PATCH /entries/{id}/status`. */
  setStatus(id: string, status: EntryStatus): Observable<Entry> {
    return this.api
      .patch<Entry>(`/entries/${id}/status`, { status })
      .pipe(tap((entry) => this.alteradosSubject.next(entry)));
  }

  /** Edita todos os campos (FR-022): `PUT /entries/{id}`; `version` vem do lançamento aberto. */
  update(id: string, input: EntryUpdate): Observable<Entry> {
    return this.api
      .put<Entry>(`/entries/${id}`, input)
      .pipe(tap((entry) => this.alteradosSubject.next(entry)));
  }

  suggestions(q: string, kind: EntryKind): Observable<EntrySuggestion[]> {
    return this.api.get<EntrySuggestion[]>(
      `/entries/suggestions?q=${encodeURIComponent(q)}&kind=${kind}`,
    );
  }
}
