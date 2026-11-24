import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { bruna, carla, ifood, tag } from '../../shared/tag-picker/testing';
import { TagsPage } from './tags.page';

describe('TagsPage', () => {
  let http: HttpTestingController;

  const mercado = tag({ id: 't-mercado', name: 'Mercado', uses90d: 3, inUse: true });
  const super_ = tag({ id: 't-super', name: 'Supermercado', uses90d: 1, inUse: true });

  function abrir(lista = [bruna, carla, ifood, mercado, super_]) {
    const fixture = TestBed.createComponent(TagsPage);
    fixture.detectChanges();
    http.expectOne('/api/v1/tags?archived=false').flush(lista);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const linha = (nome: string) =>
      el.querySelector<HTMLElement>(`[data-testid="tag"][data-nome="${nome}"]`)!;
    const clicar = (alvo: HTMLElement, id: string) => {
      alvo.querySelector<HTMLElement>(`[data-testid="${id}"]`)!.click();
      fixture.detectChanges();
    };
    const digitar = (alvo: HTMLElement, id: string, valor: string) => {
      const campo = alvo.querySelector<HTMLInputElement | HTMLSelectElement>(
        `[data-testid="${id}"]`,
      )!;
      campo.value = valor;
      campo.dispatchEvent(new Event(campo instanceof HTMLSelectElement ? 'change' : 'input'));
      fixture.detectChanges();
    };
    return { fixture, el, linha, clicar, digitar };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TagsPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista as tags com cor, "de acerto" e usos; excluir só sem uso', () => {
    const { el, linha } = abrir();
    expect(el.querySelectorAll('[data-testid="tag"]').length).toBe(5);
    expect(linha('Bruna').querySelector('[data-testid="tag-acerto"]')).not.toBeNull();
    expect(linha('Ifood').querySelector('[data-testid="tag-acerto"]')).toBeNull();
    expect(linha('Bruna').querySelector('[data-testid="tag-usos"]')!.textContent).toContain('9');
    expect(linha('Bruna').querySelector<HTMLElement>('.cor')!.style.background).toContain(
      '62, 99, 221',
    );
    // Mercado está em uso: só arquivar (US5.3); Ifood não: pode excluir
    expect(linha('Mercado').querySelector('[data-testid="excluir"]')).toBeNull();
    expect(linha('Mercado').querySelector('[data-testid="arquivar"]')).not.toBeNull();
    expect(linha('Ifood').querySelector('[data-testid="excluir"]')).not.toBeNull();
  });

  it('o filtro "Arquivadas" pede as arquivadas ao servidor', () => {
    const { fixture, el } = abrir();
    el.querySelector<HTMLInputElement>('[data-testid="filtro-arquivadas"]')!.click();
    http
      .expectOne('/api/v1/tags?archived=true')
      .flush([tag({ id: 't-velha', name: 'Velha', archived: true })]);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="arquivar"]')!.textContent).toContain('Desarquivar');
    expect(el.querySelector('[data-testid="juntar"]')).toBeNull();
  });

  it('edita nome, cor e "de acerto" enviando a versão (US5.2)', () => {
    const { fixture, linha, clicar, digitar } = abrir();
    const l = linha('Ifood');
    clicar(l, 'editar');
    digitar(l, 'campo-nome', 'Delivery');
    digitar(l, 'campo-cor', '#00aa00');
    l.querySelector<HTMLInputElement>('[data-testid="campo-acerto"]')!.click();
    fixture.detectChanges();
    clicar(l, 'salvar');

    const req = http.expectOne('/api/v1/tags/t-ifood');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      name: 'Delivery',
      color: '#00aa00',
      isSettlement: true,
      version: 1,
    });
    req.flush({ ...ifood, name: 'Delivery' });
    http.expectOne('/api/v1/tags?archived=false').flush([]);
  });

  it('marcar como de acerto com conflito lista os lançamentos (US5.2)', () => {
    const { fixture, el, linha, clicar } = abrir();
    const l = linha('Carla');
    clicar(l, 'editar');
    clicar(l, 'salvar');
    http.expectOne('/api/v1/tags/t-carla').flush(
      {
        code: 'tag.settlement_conflict',
        detail: 'Alguns lançamentos ficariam com duas tags de acerto. Corrija-os antes.',
        conflicts: [
          { type: 'expense', id: 'e1', description: 'Jantar', date: '2026-03-05' },
          { type: 'card_purchase', id: 'p1', description: 'Mercado', date: '2026-03-07' },
        ],
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="erro"]')!.textContent).toContain('duas tags de acerto');
    const itens = el.querySelectorAll('[data-testid="conflito"]');
    expect(itens.length).toBe(2);
    expect(itens[0].textContent).toContain('Jantar');
  });

  it('arquivar chama a API e recarrega', () => {
    const { linha, clicar } = abrir();
    clicar(linha('Ifood'), 'arquivar');
    const req = http.expectOne('/api/v1/tags/t-ifood/archive');
    expect(req.request.method).toBe('POST');
    req.flush({ ...ifood, archived: true });
    http.expectOne('/api/v1/tags?archived=false').flush([]);
  });

  it('exclui tag sem uso depois de confirmar', () => {
    const { linha, clicar } = abrir([bruna, tag({ id: 't-sobra', name: 'Sobra' })]);
    const l = linha('Sobra');
    clicar(l, 'excluir');
    expect(l.querySelector('[data-testid="confirmacao-exclusao"]')).not.toBeNull();
    clicar(l, 'confirmar-exclusao');
    const req = http.expectOne('/api/v1/tags/t-sobra');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/v1/tags?archived=false').flush([bruna]);
  });

  it('junta em outra tag escolhida, com confirmação (US5.1)', () => {
    const { fixture, linha, clicar, digitar } = abrir();
    const l = linha('Supermercado');
    clicar(l, 'juntar');
    const opcoes = Array.from(l.querySelectorAll('option')).map((o) => o.textContent?.trim());
    expect(opcoes).not.toContain('Supermercado');
    expect(l.querySelector<HTMLButtonElement>('[data-testid="continuar-juncao"]')!.disabled).toBe(
      true,
    );
    digitar(l, 'campo-destino', 't-mercado');
    clicar(l, 'continuar-juncao');
    expect(l.querySelector('[data-testid="confirmacao-juncao"]')!.textContent).toContain(
      '“Mercado”',
    );
    expect(http.match('/api/v1/tags/t-super/merge').length).toBe(0);
    clicar(l, 'confirmar-juncao');
    fixture.detectChanges();

    const req = http.expectOne('/api/v1/tags/t-super/merge');
    expect(req.request.body).toEqual({ targetTagId: 't-mercado' });
    req.flush(mercado);
    http.expectOne('/api/v1/tags?archived=false').flush([mercado]);
  });

  it('junção que deixaria duas tags de acerto lista os lançamentos (US5.4)', () => {
    const { fixture, el, linha, clicar, digitar } = abrir();
    const l = linha('Carla');
    clicar(l, 'juntar');
    digitar(l, 'campo-destino', 't-bruna');
    clicar(l, 'continuar-juncao');
    clicar(l, 'confirmar-juncao');
    http.expectOne('/api/v1/tags/t-carla/merge').flush(
      {
        code: 'tag.settlement_conflict',
        detail: 'Alguns lançamentos ficariam com duas tags de acerto. Corrija-os antes.',
        conflicts: [{ type: 'expense', id: 'e1', description: 'Jantar', date: '2026-03-05' }],
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="conflito"]').length).toBe(1);
  });
});
