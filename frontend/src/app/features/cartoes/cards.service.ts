import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { CreditCard, CreditCardInput, CreditCardOverview } from './card.models';

export type FiltroArquivados = 'false' | 'true' | 'all';

/** Acesso à API de cartões (`/api/v1/credit-cards`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class CardsService {
  private readonly api = inject(ApiClient);

  list(archived: FiltroArquivados = 'false'): Observable<CreditCardOverview[]> {
    return this.api.get<CreditCardOverview[]>(`/credit-cards?archived=${archived}`);
  }

  get(id: string): Observable<CreditCardOverview> {
    return this.api.get<CreditCardOverview>(`/credit-cards/${id}`);
  }

  create(input: CreditCardInput): Observable<CreditCard> {
    return this.api.post<CreditCard>('/credit-cards', input);
  }

  update(id: string, input: CreditCardInput, version: number): Observable<CreditCard> {
    return this.api.put<CreditCard>(`/credit-cards/${id}`, { ...input, version });
  }

  archive(id: string): Observable<CreditCard> {
    return this.api.post<CreditCard>(`/credit-cards/${id}/archive`, {});
  }

  unarchive(id: string): Observable<CreditCard> {
    return this.api.post<CreditCard>(`/credit-cards/${id}/unarchive`, {});
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/credit-cards/${id}`);
  }
}
