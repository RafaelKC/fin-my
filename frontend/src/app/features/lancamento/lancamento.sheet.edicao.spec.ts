import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { carteira } from '../carteiras/testing';
import { Entry } from './entry.models';
import { LancamentoSheet } from './lancamento.sheet';

const original: Entry = {
  id: 'e1',
  kind: 'expense',
  description: 'Tim',
  amountCents: 4499,
  date: '2026-03-10',
  referenceMonth: '2026-03',
  status: 'planned',
  walletId: 'w-azul',
  createdAt: '',
  version: 3,
};

describe('LancamentoSheet em edição', () => {
  it('abre com os dados e salva por PUT com a version', async () => {
    const fechar = vi.fn();
    TestBed.configureTestingModule({
      imports: [LancamentoSheet],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: fechar } },
        { provide: MAT_DIALOG_DATA, useValue: { entry: original } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
    const fixture = TestBed.createComponent(LancamentoSheet);
    const el: HTMLElement = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/wallets?archived=all').flush([carteira()]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('h2')?.textContent).toContain('Editar lançamento');
    expect(el.querySelector('[data-testid="salvar-outro"]')).toBeNull();
    expect(el.querySelector<HTMLInputElement>('#lancamento-descricao')!.value).toBe('Tim');

    el.querySelector<HTMLButtonElement>('[data-testid="salvar"]')!.click();
    const put = http.expectOne('/api/v1/entries/e1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({
      description: 'Tim',
      amount: '44,99',
      status: 'planned',
      walletId: 'w-azul',
      version: 3,
    });
    put.flush({ ...original, version: 4 });
    expect(fechar).toHaveBeenCalledWith(expect.objectContaining({ version: 4 }));
    http.verify();
  });
});
