import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject } from 'rxjs';
import { carteira } from '../carteiras/testing';
import { Entry } from './entry.models';
import { LancamentoSheet, MENSAGEM_CONFLITO } from './lancamento.sheet';

const original: Entry = {
  id: 'e1',
  kind: 'expense',
  description: 'Tim',
  amountCents: 4499,
  date: '2026-03-10',
  referenceMonth: '2026-03',
  status: 'settled',
  walletId: 'w-azul',
  createdAt: '',
  version: 3,
};

describe('LancamentoSheet: editar, duplicar e excluir (US6)', () => {
  let http: HttpTestingController;
  let fechar: ReturnType<typeof vi.fn>;
  let snack: ReturnType<typeof vi.spyOn>;
  let el: HTMLElement;
  let acao$: Subject<void>;

  async function abrir(): Promise<void> {
    fechar = vi.fn();
    acao$ = new Subject<void>();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: fechar } },
        { provide: MAT_DIALOG_DATA, useValue: { entry: original } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    snack = vi
      .spyOn(TestBed.inject(MatSnackBar), 'open')
      .mockReturnValue({ onAction: () => acao$ } as never);
    const fixture = TestBed.createComponent(LancamentoSheet);
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    await fixture.whenStable();
    fixture.detectChanges();
    (el as unknown as { fixture: typeof fixture }).fixture = fixture;
  }

  const tela = () =>
    (el as unknown as { fixture: { detectChanges(): void; whenStable(): Promise<void> } }).fixture;
  const clicar = (testid: string) =>
    el.querySelector<HTMLButtonElement>(`[data-testid="${testid}"]`)!.click();

  it('mostra Salvar, Duplicar e Excluir na edição', async () => {
    await abrir();
    for (const id of ['salvar', 'duplicar', 'excluir']) {
      expect(el.querySelector(`[data-testid="${id}"]`)).not.toBeNull();
    }
    http.verify();
  });

  it('409 mostra "alterado em outro aparelho", recarrega e salva com a nova version', async () => {
    await abrir();
    clicar('salvar');
    http
      .expectOne('/api/v1/entries/e1')
      .flush(
        { code: 'concurrency_conflict', detail: 'Este lançamento foi alterado em outro aparelho.' },
        { status: 409, statusText: 'Conflict' },
      );
    const recarga = http.expectOne('/api/v1/entries/e1');
    expect(recarga.request.method).toBe('GET');
    recarga.flush({ ...original, description: 'Tim Móvel', amountCents: 5000, version: 4 });
    tela().detectChanges();
    await tela().whenStable();
    tela().detectChanges();

    expect(el.querySelector('[data-testid="erro-lancamento"]')?.textContent).toContain(
      MENSAGEM_CONFLITO,
    );
    expect(MENSAGEM_CONFLITO).toContain('alterado em outro aparelho');
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Tim Móvel');
    expect(fechar).not.toHaveBeenCalled();

    clicar('salvar');
    const put = http.expectOne('/api/v1/entries/e1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({ description: 'Tim Móvel', version: 4 });
    put.flush({ ...original, version: 5 });
    expect(fechar).toHaveBeenCalled();
    http.verify();
  });

  it('409 com lançamento já excluído fecha o formulário', async () => {
    await abrir();
    clicar('salvar');
    http
      .expectOne('/api/v1/entries/e1')
      .flush({ code: 'concurrency_conflict' }, { status: 409, statusText: 'Conflict' });
    http
      .expectOne('/api/v1/entries/e1')
      .flush({ code: 'not_found' }, { status: 404, statusText: 'Not Found' });
    expect(fechar).toHaveBeenCalledWith(undefined);
    http.verify();
  });

  it('Duplicar pede só a nova data e fecha com o novo lançamento (US6.4)', async () => {
    await abrir();
    clicar('duplicar');
    tela().detectChanges();
    const campo = el.querySelector<HTMLInputElement>('#lancamento-data-duplicada')!;
    campo.value = '2026-04-10';
    campo.dispatchEvent(new Event('input'));
    clicar('duplicar-confirmar');

    const post = http.expectOne('/api/v1/entries/e1/duplicate');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ date: '2026-04-10' });
    expect(post.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    const copia = { ...original, id: 'e2', date: '2026-04-10', referenceMonth: '2026-04' };
    post.flush(copia);
    expect(fechar).toHaveBeenCalledWith(expect.objectContaining({ id: 'e2' }));
    http.verify();
  });

  it('Duplicar mostra o erro da API (carteira arquivada) e mantém o painel', async () => {
    await abrir();
    clicar('duplicar');
    tela().detectChanges();
    clicar('duplicar-confirmar');
    http
      .expectOne('/api/v1/entries/e1/duplicate')
      .flush(
        { code: 'wallet.archived', detail: 'A carteira está arquivada.' },
        { status: 422, statusText: 'Unprocessable' },
      );
    tela().detectChanges();
    expect(el.querySelector('[data-testid="erro-duplicar"]')?.textContent).toContain(
      'A carteira está arquivada.',
    );
    expect(fechar).not.toHaveBeenCalled();
    http.verify();
  });

  it('Excluir fecha o formulário e oferece "Desfazer" por 8 s (RT-09, US6.3)', async () => {
    await abrir();
    clicar('excluir');
    const del = http.expectOne('/api/v1/entries/e1');
    expect(del.request.method).toBe('DELETE');
    del.flush({ deletionBatchId: 'lote-1' });
    expect(fechar).toHaveBeenCalledWith(undefined);
    expect(snack).toHaveBeenCalledWith(
      'Excluído',
      'Desfazer',
      expect.objectContaining({ duration: 8000 }),
    );

    acao$.next();
    const restore = http.expectOne('/api/v1/deletions/lote-1/restore');
    expect(restore.request.method).toBe('POST');
    restore.flush(null, { status: 204, statusText: 'No Content' });
    http.verify();
  });
});
