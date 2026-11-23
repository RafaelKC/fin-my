import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WalletInput } from './wallet.models';
import { WalletsService } from './wallets.service';

describe('WalletsService', () => {
  let service: WalletsService;
  let http: HttpTestingController;
  const input: WalletInput = {
    name: 'Azul',
    type: 'checking',
    color: '#1E88E5',
    initialBalance: '500,00',
    initialBalanceDate: '2026-03-01',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WalletsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista ativas por padrão e arquivadas quando pedido', () => {
    service.list().subscribe();
    http.expectOne('/api/v1/wallets?archived=false').flush([]);
    service.list('true').subscribe();
    http.expectOne('/api/v1/wallets?archived=true').flush([]);
  });

  it('cria com POST e edita com PUT incluindo a versão', () => {
    service.create(input).subscribe();
    const post = http.expectOne('/api/v1/wallets');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(input);
    post.flush({});

    service.update('w1', input, 3).subscribe();
    const put = http.expectOne('/api/v1/wallets/w1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ ...input, version: 3 });
    put.flush({});
  });

  it('torna principal, desarquiva e arquiva (com ou sem nova principal)', () => {
    service.makePrimary('w1').subscribe();
    expect(http.expectOne('/api/v1/wallets/w1/make-primary').request.method).toBe('POST');
    service.unarchive('w1').subscribe();
    http.expectOne('/api/v1/wallets/w1/unarchive').flush({});

    service.archive('w1').subscribe();
    expect(http.expectOne('/api/v1/wallets/w1/archive').request.body).toEqual({});
    service.archive('w1', 'w2').subscribe();
    expect(http.expectOne('/api/v1/wallets/w1/archive').request.body).toEqual({
      newPrimaryWalletId: 'w2',
    });
  });

  it('exclui com DELETE, passando a nova principal na query quando houver', () => {
    service.delete('w1').subscribe();
    const a = http.expectOne('/api/v1/wallets/w1');
    expect(a.request.method).toBe('DELETE');
    a.flush(null);
    service.delete('w1', 'w2').subscribe();
    http.expectOne('/api/v1/wallets/w1?newPrimaryWalletId=w2').flush(null);
  });
});
