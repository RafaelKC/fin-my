import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MonthsService } from './months.service';

describe('MonthsService', () => {
  it('chama GET /months/{mes} com e sem filtro de carteiras e de tags', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const s = TestBed.inject(MonthsService);
    s.get('2026-03').subscribe();
    http.expectOne('/api/v1/months/2026-03').flush({});
    s.get('2026-03', ['a', 'b']).subscribe();
    http.expectOne('/api/v1/months/2026-03?walletIds=a,b').flush({});
    s.get('2026-03', [], ['t1', 't2']).subscribe();
    http.expectOne('/api/v1/months/2026-03?tagIds=t1,t2').flush({});
    s.get('2026-03', ['a'], ['t1']).subscribe();
    http.expectOne('/api/v1/months/2026-03?walletIds=a&tagIds=t1').flush({});
  });
});
