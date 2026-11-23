import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject } from 'rxjs';
import { ExclusaoService } from './exclusao.service';

describe('ExclusaoService', () => {
  let service: ExclusaoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ExclusaoService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('exclui transferência por DELETE /transfers/{id} e avisa em mudou$', () => {
    let avisos = 0;
    service.mudou$.subscribe(() => avisos++);
    let lote = '';
    service.excluir('transfer', 't1').subscribe((r) => (lote = r.deletionBatchId));
    const req = http.expectOne('/api/v1/transfers/t1');
    expect(req.request.method).toBe('DELETE');
    req.flush({ deletionBatchId: 'lote-9' });
    expect(lote).toBe('lote-9');
    expect(avisos).toBe(1);
  });

  it('o aviso dura 8 s e, sem tocar em Desfazer, nada é restaurado', () => {
    const snack = vi
      .spyOn(TestBed.inject(MatSnackBar), 'open')
      .mockReturnValue({ onAction: () => new Subject<void>() } as never);
    service.oferecerDesfazer('lote-1');
    expect(snack).toHaveBeenCalledWith(
      'Excluído',
      'Desfazer',
      expect.objectContaining({ duration: 8000 }),
    );
  });

  it('desfazer expirado mostra a mensagem da API', () => {
    const acao$ = new Subject<void>();
    const snack = vi
      .spyOn(TestBed.inject(MatSnackBar), 'open')
      .mockReturnValue({ onAction: () => acao$ } as never);
    service.oferecerDesfazer('lote-1');
    acao$.next();
    http
      .expectOne('/api/v1/deletions/lote-1/restore')
      .flush(
        { code: 'deletion.not_found', detail: 'Não foi possível desfazer.' },
        { status: 404, statusText: 'Not Found' },
      );
    expect(snack).toHaveBeenLastCalledWith(
      'Não foi possível desfazer.',
      'Fechar',
      expect.anything(),
    );
  });
});
