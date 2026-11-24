import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  EXPORTAR_MAX_MESES,
  ExportarLancamentos,
  mesesNoIntervalo,
  nomeDoArquivo,
} from './exportar-lancamentos';

describe('mesesNoIntervalo / nomeDoArquivo', () => {
  it('conta meses inclusive e rejeita formato inválido', () => {
    expect(mesesNoIntervalo('2026-03', '2026-03')).toBe(1);
    expect(mesesNoIntervalo('2025-11', '2026-02')).toBe(4);
    expect(mesesNoIntervalo('2026-03', '2026-01')).toBe(-1);
    expect(mesesNoIntervalo('', '2026-01')).toBeNull();
  });

  it('lê o nome do arquivo do Content-Disposition', () => {
    expect(nomeDoArquivo('attachment; filename="fin-my-2026-03_2026-03.csv"')).toBe(
      'fin-my-2026-03_2026-03.csv',
    );
    expect(nomeDoArquivo("attachment; filename=a.csv; filename*=UTF-8''b%20c.csv")).toBe('a.csv');
    expect(nomeDoArquivo(null)).toBeNull();
  });
});

describe('ExportarLancamentos', () => {
  let fixture: ComponentFixture<ExportarLancamentos>;
  let http: HttpTestingController;
  let el: HTMLElement;
  let baixados: string[];

  const preencher = (id: string, valor: string) => {
    const campo = el.querySelector<HTMLInputElement>(id)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  };
  const exportar = () => {
    el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ExportarLancamentos],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExportarLancamentos);
    el = fixture.nativeElement;
    fixture.detectChanges();

    baixados = [];
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:teste');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      baixados.push(this.download);
    });
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('baixa o CSV do intervalo com o nome do cabeçalho', () => {
    preencher('#exportar-de', '2026-03');
    preencher('#exportar-ate', '2026-04');
    exportar();

    const req = http.expectOne('/api/v1/exports/entries.csv?from=2026-03&to=2026-04');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['Tipo;Descrição']), {
      headers: { 'Content-Disposition': 'attachment; filename="fin-my-2026-03_2026-04.csv"' },
    });
    fixture.detectChanges();

    expect(baixados).toEqual(['fin-my-2026-03_2026-04.csv']);
    expect(el.querySelector('[data-testid="exportado"]')!.textContent).toContain(
      'fin-my-2026-03_2026-04.csv',
    );
  });

  it('mês final anterior ao inicial não chama a API', () => {
    preencher('#exportar-de', '2026-04');
    preencher('#exportar-ate', '2026-03');
    exportar();
    expect(el.querySelector('[data-testid="erro-exportar"]')!.textContent).toContain(
      'não pode ser anterior',
    );
    http.expectNone(() => true);
  });

  it(`mais de ${EXPORTAR_MAX_MESES} meses não chama a API`, () => {
    preencher('#exportar-de', '2010-01');
    preencher('#exportar-ate', '2026-01');
    exportar();
    expect(el.querySelector('[data-testid="erro-exportar"]')!.textContent).toContain('120 meses');
    http.expectNone(() => true);
  });

  it('mostra o detalhe do erro 422 do servidor', async () => {
    preencher('#exportar-de', '2026-03');
    preencher('#exportar-ate', '2026-03');
    exportar();
    http
      .expectOne('/api/v1/exports/entries.csv?from=2026-03&to=2026-03')
      .flush(
        new Blob([JSON.stringify({ code: 'export.range_invalid', detail: 'Período inválido' })]),
        {
          status: 422,
          statusText: 'Unprocessable',
        },
      );
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelector('[data-testid="erro-exportar"]')!.textContent).toContain(
        'Período inválido',
      );
    });
    expect(baixados).toEqual([]);
  });
});
