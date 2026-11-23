import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { MonthView } from './month.models';

/** Acesso à visão do mês (`GET /api/v1/months/{month}`, contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class MonthsService {
  private readonly api = inject(ApiClient);

  /** `walletIds` vazio = todas as carteiras (ativas e arquivadas). */
  get(month: string, walletIds: readonly string[] = []): Observable<MonthView> {
    const query =
      walletIds.length > 0 ? `?walletIds=${walletIds.map(encodeURIComponent).join(',')}` : '';
    return this.api.get<MonthView>(`/months/${month}${query}`);
  }
}
