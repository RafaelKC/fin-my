import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { NowView, YearPanel } from './panel.models';

/** Acesso ao Painel do ano e ao resumo de agora (006). */
@Injectable({ providedIn: 'root' })
export class PanelService {
  private readonly api = inject(ApiClient);

  year(year: number): Observable<YearPanel> {
    return this.api.get<YearPanel>(`/years/${year}/panel`);
  }

  now(): Observable<NowView> {
    return this.api.get<NowView>('/now');
  }
}
