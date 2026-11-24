import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OnboardingService } from './onboarding.service';

describe('OnboardingService', () => {
  let http: HttpTestingController;
  let service: OnboardingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(OnboardingService);
  });
  afterEach(() => http.verify());

  it('lê o estado, marca etapa e dispensa o guia', () => {
    service.get().subscribe();
    http.expectOne({ method: 'GET', url: '/api/v1/onboarding' }).flush({});

    service.setStep('cards', 'skipped').subscribe();
    const put = http.expectOne({ method: 'PUT', url: '/api/v1/onboarding/steps/cards' });
    expect(put.request.body).toEqual({ state: 'skipped' });
    put.flush({});

    service.dismiss().subscribe();
    http.expectOne({ method: 'POST', url: '/api/v1/onboarding/dismiss' }).flush({});
  });
});
