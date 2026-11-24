import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import {
  PlanningCellInput,
  PlanningCellResult,
  PlanningFillInput,
  PlanningFillResult,
  PlanningGrid,
} from './planning.models';

/** Acesso à grade de planejamento do ano (`/api/v1/years/{yyyy}/planning`, 007). */
@Injectable({ providedIn: 'root' })
export class PlanningService {
  private readonly api = inject(ApiClient);

  grid(year: number): Observable<PlanningGrid> {
    return this.api.get<PlanningGrid>(`/years/${year}/planning`);
  }

  setCell(year: number, input: PlanningCellInput): Observable<PlanningCellResult> {
    return this.api.put<PlanningCellResult>(`/years/${year}/planning/cells`, input);
  }

  /** `idempotencyKey`: uma por diálogo, reaproveitada em reenvios. */
  fill(
    year: number,
    input: PlanningFillInput,
    idempotencyKey: string,
  ): Observable<PlanningFillResult> {
    return this.api.post<PlanningFillResult>(`/years/${year}/planning/fill`, input, {
      'Idempotency-Key': idempotencyKey,
    });
  }
}
