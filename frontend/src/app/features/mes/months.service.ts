import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { MonthView } from './month.models';

/** Acesso à visão do mês (`GET /api/v1/months/{month}`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class MonthsService {
  private readonly api = inject(ApiClient);

  /** `walletIds` vazio = todas as carteiras (ativas e arquivadas); `tagIds` vazio = sem filtro de tags. */
  get(
    month: string,
    walletIds: readonly string[] = [],
    tagIds: readonly string[] = [],
  ): Observable<MonthView> {
    const partes: string[] = [];
    if (walletIds.length > 0)
      partes.push(`walletIds=${walletIds.map(encodeURIComponent).join(',')}`);
    if (tagIds.length > 0) partes.push(`tagIds=${tagIds.map(encodeURIComponent).join(',')}`);
    return this.api.get<MonthView>(
      `/months/${month}${partes.length ? `?${partes.join('&')}` : ''}`,
    );
  }
}
