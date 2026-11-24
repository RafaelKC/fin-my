import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { SettlementItem, SettlementRow } from './settlement.models';

/** Acesso à API de acertos (`/api/v1/settlements`, 004 research R3). */
@Injectable({ providedIn: 'root' })
export class SettlementsService {
  private readonly api = inject(ApiClient);

  /** Uma linha por Tag de acerto ativa, mais as arquivadas com algum valor no mês. */
  list(month: string): Observable<SettlementRow[]> {
    return this.api.get<SettlementRow[]>(`/settlements/${month}`);
  }

  /** Lançamentos que compõem os números da tag no mês (`spent` e `charged`). */
  items(month: string, tagId: string): Observable<SettlementItem[]> {
    return this.api.get<SettlementItem[]>(`/settlements/${month}/tags/${tagId}/items`);
  }
}
