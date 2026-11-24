import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TagPicker } from './tag-picker';
import { Tag } from './tag.models';
import { bruna, carla, ifood, tag } from './testing';

@Component({
  imports: [TagPicker],
  template: `<app-tag-picker
    [(ids)]="ids"
    [dividirDisponivel]="dividir"
    (alterado)="alteracoes = alteracoes + 1"
    (dividir)="atalhos = atalhos + 1"
  />`,
})
class Hospedeiro {
  readonly ids = signal<string[]>([]);
  dividir = false;
  alteracoes = 0;
  atalhos = 0;
}

describe('TagPicker (004 US1)', () => {
  let fixture: ComponentFixture<Hospedeiro>;
  let el: HTMLElement;
  let http: HttpTestingController;

  const oito = Array.from({ length: 10 }, (_, i) =>
    tag({ id: `t${i}`, name: `Tag ${String(i).padStart(2, '0')}`, uses90d: 100 - i }),
  );

  async function atualizar() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function abrir(tags: Tag[], ids: string[] = [], dividir = false) {
    TestBed.configureTestingModule({
      imports: [Hospedeiro],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Hospedeiro);
    fixture.componentInstance.ids.set(ids);
    fixture.componentInstance.dividir = dividir;
    el = fixture.nativeElement;
    fixture.detectChanges();
    http.expectOne('/api/v1/tags?archived=all').flush(tags);
    await atualizar();
  }
  const chips = () => Array.from(el.querySelectorAll<HTMLElement>('[data-testid="tag-chip"]'));
  const chip = (nome: string) => chips().find((c) => c.dataset['tag'] === nome)!;
  const ids = () => fixture.componentInstance.ids();
  async function digitar(texto: string) {
    const campo = el.querySelector<HTMLInputElement>('[data-testid="tag-busca"]')!;
    campo.value = texto;
    campo.dispatchEvent(new Event('input'));
    await atualizar();
  }
  async function enter() {
    el.querySelector('[data-testid="tag-busca"]')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter' }),
    );
    await atualizar();
  }
  async function clicar(seletor: string) {
    el.querySelector<HTMLElement>(seletor)!.click();
    await atualizar();
  }

  afterEach(() => http.verify());

  it('mostra as 8 mais usadas como chips, na ordem de uso (US1.1)', async () => {
    await abrir(oito);
    expect(chips().map((c) => c.dataset['tag'])).toEqual(oito.slice(0, 8).map((t) => t.name));
  });

  it('um toque numa chip escolhe e outro tira (SC-001)', async () => {
    await abrir([ifood, bruna]);
    chip('Ifood').click();
    await atualizar();
    expect(ids()).toEqual(['t-ifood']);
    expect(fixture.componentInstance.alteracoes).toBe(1);
    expect(chip('Ifood').getAttribute('aria-pressed')).toBe('true');
    chip('Ifood').click();
    await atualizar();
    expect(ids()).toEqual([]);
  });

  it('tags de acerto levam o ícone de pessoa', async () => {
    await abrir([ifood, bruna]);
    expect(chip('Bruna').querySelector('mat-icon')?.textContent).toContain('person');
    expect(chip('Ifood').querySelector('mat-icon')).toBeNull();
  });

  it('a busca filtra as demais tags e escolhe uma delas', async () => {
    await abrir(oito);
    await digitar('Tag 09');
    const resultados = el.querySelectorAll<HTMLElement>('[data-testid="tag-resultado"]');
    expect(resultados).toHaveLength(1);
    resultados[0].click();
    await atualizar();
    expect(ids()).toEqual(['t9']);
    // a escolhida vira chip
    expect(chip('Tag 09').getAttribute('aria-pressed')).toBe('true');
  });

  it('texto sem correspondência oferece "Criar tag" e cria uma tag comum (US1.2)', async () => {
    await abrir([ifood]);
    await digitar('Farmácia');
    expect(el.querySelector('[data-testid="tag-criar"]')?.textContent).toContain(
      'Criar tag "Farmácia"',
    );

    await clicar('[data-testid="tag-criar"]');
    const req = http.expectOne('/api/v1/tags');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Farmácia' });
    req.flush(tag({ id: 't-farmacia', name: 'Farmácia' }), { status: 201, statusText: 'Created' });
    await atualizar();

    expect(ids()).toEqual(['t-farmacia']);
    expect(chip('Farmácia').getAttribute('aria-pressed')).toBe('true');
    expect(el.querySelector<HTMLInputElement>('[data-testid="tag-busca"]')!.value).toBe('');
  });

  it('digitar "bruna" usa a existente em vez de criar outra (US1.3)', async () => {
    await abrir([ifood, bruna]);
    await digitar('bruna');
    expect(el.querySelector('[data-testid="tag-criar"]')).toBeNull();
    await enter();
    expect(ids()).toEqual(['t-bruna']);
  });

  it('se outro aparelho criou o nome antes (409), usa a existente', async () => {
    await abrir([ifood]);
    await digitar('Lia');
    await clicar('[data-testid="tag-criar"]');
    http
      .expectOne('/api/v1/tags')
      .flush(
        { code: 'tag.name_taken', detail: 'Já existe.', existingTagId: 't-lia' },
        { status: 409, statusText: 'Conflict' },
      );
    http.expectOne('/api/v1/tags?archived=all').flush([ifood, tag({ id: 't-lia', name: 'Lia' })]);
    await atualizar();
    expect(ids()).toEqual(['t-lia']);
  });

  it('segunda tag de acerto é recusada com o aviso de Dividir (US1.4)', async () => {
    await abrir([bruna, carla, ifood], ['t-bruna'], true);
    chip('Carla').click();
    await atualizar();
    expect(ids()).toEqual(['t-bruna']);
    expect(el.querySelector('[data-testid="aviso-duas-de-acerto"]')?.textContent).toContain(
      'Só uma tag de acerto por lançamento. Usar Dividir?',
    );
    await clicar('[data-testid="tag-dividir"]');
    expect(fixture.componentInstance.atalhos).toBe(1);
  });

  it('sem Dividir disponível o aviso aparece sem o atalho', async () => {
    await abrir([bruna, carla], ['t-bruna']);
    chip('Carla').click();
    await atualizar();
    expect(el.querySelector('[data-testid="aviso-duas-de-acerto"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="tag-dividir"]')).toBeNull();
  });

  it('tira a de acerto e então aceita outra', async () => {
    await abrir([bruna, carla], ['t-bruna']);
    chip('Bruna').click();
    await atualizar();
    chip('Carla').click();
    await atualizar();
    expect(ids()).toEqual(['t-carla']);
    expect(el.querySelector('[data-testid="aviso-duas-de-acerto"]')).toBeNull();
  });

  it('tag arquivada já no lançamento continua escolhida e removível, mas não volta', async () => {
    const velha = tag({ id: 't-velha', name: 'Velha', archived: true });
    await abrir([ifood, velha], ['t-velha']);
    expect(chip('Velha').getAttribute('aria-pressed')).toBe('true');
    chip('Velha').click();
    await atualizar();
    expect(ids()).toEqual([]);
    // fora das chips (não é ativa), nem aparece na busca
    await digitar('velha');
    expect(el.querySelectorAll('[data-testid="tag-resultado"]')).toHaveLength(0);
  });

  it('descarta ids que não existem mais (ex.: rascunho antigo)', async () => {
    await abrir([ifood], ['t-ifood', 't-sumiu']);
    expect(ids()).toEqual(['t-ifood']);
  });

  it('limita a 20 tags', async () => {
    const muitas = Array.from({ length: 21 }, (_, i) => tag({ id: `m${i}`, name: `M${i}` }));
    await abrir(
      muitas,
      muitas.slice(0, 20).map((t) => t.id),
    );
    await digitar('M20');
    await clicar('[data-testid="tag-resultado"]');
    expect(ids()).toHaveLength(20);
    expect(el.querySelector('[data-testid="erro-tag"]')?.textContent).toContain('No máximo 20');
  });
});
