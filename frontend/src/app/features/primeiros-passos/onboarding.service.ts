import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { EstadoEtapa, EtapaChave, OnboardingState } from './onboarding.models';

/** Estado do guia de primeiros passos do Espaço (006 FR-007). */
@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly api = inject(ApiClient);

  get(): Observable<OnboardingState> {
    return this.api.get<OnboardingState>('/onboarding');
  }

  setStep(step: EtapaChave, state: EstadoEtapa): Observable<OnboardingState> {
    return this.api.put<OnboardingState>(`/onboarding/steps/${step}`, { state });
  }

  dismiss(): Observable<OnboardingState> {
    return this.api.post<OnboardingState>('/onboarding/dismiss');
  }
}
