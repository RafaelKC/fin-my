import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PanelService } from './panel.service';

describe('PanelService', () => {
  it('busca o painel do ano e o resumo de agora', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(PanelService);
    service.year(2026).subscribe();
    http.expectOne('/api/v1/years/2026/panel').flush({});
    service.now().subscribe();
    http.expectOne('/api/v1/now').flush({});
    http.verify();
  });
});
