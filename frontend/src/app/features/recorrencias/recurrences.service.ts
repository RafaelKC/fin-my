import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import {
  Recurrence,
  RecurrenceDeleted,
  RecurrenceEnded,
  RecurrenceFromItem,
  RecurrenceInput,
  RecurrenceList,
  RecurrenceStatus,
  RecurrenceUpdate,
  RecurrenceUpdated,
} from './recurrence.models';

/** Acesso à API de recorrências (`/api/v1/recurrences`, specs/005-recorrencias/contracts/api.md). */
@Injectable({ providedIn: 'root' })
export class RecurrencesService {
  private readonly api = inject(ApiClient);

  list(status: RecurrenceStatus | 'all' = 'active'): Observable<RecurrenceList> {
    return this.api.get<RecurrenceList>(`/recurrences?status=${status}`);
  }

  get(id: string): Observable<Recurrence> {
    return this.api.get<Recurrence>(`/recurrences/${id}`);
  }

  /** `idempotencyKey`: uma por formulário, reaproveitada em reenvios. */
  create(input: RecurrenceInput, idempotencyKey: string): Observable<Recurrence> {
    return this.api.post<Recurrence>('/recurrences', input, { 'Idempotency-Key': idempotencyKey });
  }

  /** "Repetir todo mês" a partir de um lançamento (US4); uma `idempotencyKey` por tentativa. */
  fromItem(input: RecurrenceFromItem, idempotencyKey: string): Observable<Recurrence> {
    return this.api.post<Recurrence>('/recurrences/from-item', input, {
      'Idempotency-Key': idempotencyKey,
    });
  }

  update(id: string, input: RecurrenceUpdate): Observable<RecurrenceUpdated> {
    return this.api.put<RecurrenceUpdated>(`/recurrences/${id}`, input);
  }

  end(id: string, fromMonth: string): Observable<RecurrenceEnded> {
    return this.api.post<RecurrenceEnded>(`/recurrences/${id}/end`, { fromMonth });
  }

  delete(id: string): Observable<RecurrenceDeleted> {
    return this.api.delete<RecurrenceDeleted>(`/recurrences/${id}`);
  }
}
