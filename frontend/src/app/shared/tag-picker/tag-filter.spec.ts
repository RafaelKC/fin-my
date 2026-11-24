import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TagFilter } from './tag-filter';
import { bruna, ifood, tag } from './testing';

@Component({
  imports: [TagFilter],
  template: `<app-tag-filter [(ids)]="ids" />`,
})
class Hospedeiro {
  readonly ids = signal<string[]>([]);
}

describe('TagFilter (004 FR-004)', () => {
  let fixture: ComponentFixture<Hospedeiro>;
  let el: HTMLElement;
  let http: HttpTestingController;

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function iniciar(ids: string[] = []) {
    TestBed.configureTestingModule({
      imports: [Hospedeiro],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Hospedeiro);
    fixture.componentInstance.ids.set(ids);
    el = fixture.nativeElement;
    await atualizar();
  }
  const abrirPainel = async () => {
    el.querySelector<HTMLElement>('[data-testid="filtro-tags-abrir"]')!.click();
    await atualizar();
  };

  afterEach(() => http.verify());

  it('não pede as tags antes de abrir o painel', async () => {
    await iniciar();
    http.expectNone('/api/v1/tags?archived=all');
    expect(el.querySelector('[data-testid="filtro-tags-abrir"]')?.textContent).toContain('Tags');
  });

  it('escolhe várias tags e limpa; o botão mostra a quantidade', async () => {
    await iniciar();
    await abrirPainel();
    http
      .expectOne('/api/v1/tags?archived=all')
      .flush([ifood, bruna, tag({ id: 't-velha', name: 'Velha', archived: true })]);
    await atualizar();

    const filtro = (nome: string) =>
      el.querySelector<HTMLElement>(`[data-testid="filtro-tag"][data-tag="${nome}"]`)!;
    expect(el.querySelectorAll('[data-testid="filtro-tag"]')).toHaveLength(3);
    // arquivada continua filtrável: ela segue nos lançamentos antigos
    expect(filtro('Velha').textContent).toContain('arquivada');

    filtro('Bruna').click();
    await atualizar();
    filtro('Ifood').click();
    await atualizar();
    expect(fixture.componentInstance.ids()).toEqual(['t-bruna', 't-ifood']);
    expect(el.querySelector('[data-testid="filtro-tags-abrir"]')?.textContent).toContain(
      'Tags (2)',
    );

    filtro('Bruna').click();
    await atualizar();
    expect(fixture.componentInstance.ids()).toEqual(['t-ifood']);

    el.querySelector<HTMLElement>('[data-testid="filtro-tags-limpar"]')!.click();
    await atualizar();
    expect(fixture.componentInstance.ids()).toEqual([]);
  });

  it('pede a lista uma só vez, ao reabrir não repete', async () => {
    await iniciar(['t-bruna']);
    await abrirPainel();
    http.expectOne('/api/v1/tags?archived=all').flush([bruna]);
    await abrirPainel(); // fecha
    await abrirPainel(); // reabre
    http.expectNone('/api/v1/tags?archived=all');
  });

  it('avisa quando não há tags', async () => {
    await iniciar();
    await abrirPainel();
    http.expectOne('/api/v1/tags?archived=all').flush([]);
    await atualizar();
    expect(el.querySelector('[data-testid="filtro-tags-vazio"]')).not.toBeNull();
  });
});
