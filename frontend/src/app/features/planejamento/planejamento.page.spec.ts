import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { carteira } from '../carteiras/testing';
import { PlanejamentoPage } from './planejamento.page';
import { PlanningGrid } from './planning.models';
import { faturaAzul, grade2027, linha, totais } from './testing';

const MESES_CELULA = (el: Element, mes: string) =>
  el.querySelector(`[data-testid="celula"][data-mes="${mes}"]`)!;

describe('PlanejamentoPage', () => {
  let http: HttpTestingController;
  const params = new BehaviorSubject(convertToParamMap({ ano: '2027' }));
  const original = window.matchMedia;

  async function abrir(grade: PlanningGrid = grade2027(), coarse = false) {
    window.matchMedia = ((q: string) =>
      ({
        matches: q.includes('min-width') ? !coarse : false,
        media: q,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
      }) as unknown as MediaQueryList) as typeof matchMedia;
    const fixture = TestBed.createComponent(PlanejamentoPage);
    fixture.detectChanges();
    http
      .expectOne('/api/v1/wallets?archived=false')
      .flush([carteira(), carteira({ id: 'w-verde', name: 'Verde', isPrimary: false })]);
    http.expectOne('/api/v1/years/2027/planning').flush(grade);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const limpo = (e: Element | null | undefined) => e?.textContent?.replace(/\s+/g, ' ').trim();
    const linhaDe = (id: string) => el.querySelector(`[data-testid="linha"][data-linha="${id}"]`)!;
    const celulaDe = (id: string, mes: string) => MESES_CELULA(linhaDe(id), mes);
    const abrirCelula = async (id: string, mes: string) => {
      (celulaDe(id, mes).querySelector('button.valor') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      return celulaDe(id, mes).querySelector('input') as HTMLInputElement;
    };
    const digitar = (input: HTMLInputElement, texto: string, tecla = 'Enter') => {
      input.value = texto;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: tecla, bubbles: true }));
      fixture.detectChanges();
    };
    const atualizar = async () => {
      fixture.detectChanges();
      TestBed.inject(ApplicationRef).tick();
      await fixture.whenStable();
      fixture.detectChanges();
      TestBed.inject(ApplicationRef).tick();
    };
    /** O MatDialog só entrega `afterClosed` depois da animação de saída. */
    const aguardarFechar = async () => {
      await new Promise((r) => setTimeout(r, 300));
      await atualizar();
    };
    return {
      fixture,
      el,
      limpo,
      linhaDe,
      celulaDe,
      abrirCelula,
      digitar,
      atualizar,
      aguardarFechar,
    };
  }

  beforeEach(() => {
    params.next(convertToParamMap({ ano: '2027' }));
    TestBed.configureTestingModule({
      imports: [PlanejamentoPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    window.matchMedia = original;
    TestBed.inject(MatDialog).closeAll();
    http.verify();
  });

  it('US1.1: blocos Entradas, Saídas e Faturas, 12 colunas e as linhas de total', async () => {
    const { el, limpo, linhaDe } = await abrir();
    expect(limpo(el.querySelector('[data-testid="planejamento-titulo"]'))).toBe(
      'Planejamento 2027',
    );
    expect(el.querySelectorAll('[data-testid="mes-coluna"]')).toHaveLength(12);
    const nomes = (bloco: string) =>
      Array.from(
        el.querySelectorAll(`[data-testid="bloco-${bloco}"] [data-testid="linha-nome"]`),
      ).map((n) => limpo(n));
    expect(nomes('entradas')).toEqual(['Salário']);
    expect(nomes('saidas')).toEqual(['Natal', 'Tim']);
    expect(nomes('faturas')).toEqual(['Fatura Azul']);
    expect(limpo(linhaDe('income:salario').querySelector('[data-mes="2027-01"]'))).toContain(
      '5.000,00',
    );
    for (const t of ['entradas', 'saidas', 'resultado', 'saldo']) {
      expect(el.querySelectorAll(`[data-testid="total-${t}"] td`)).toHaveLength(12);
    }
    expect(
      limpo(el.querySelector('[data-testid="total-resultado"] [data-mes="2027-01"]')),
    ).toContain('3.000,00');
    expect(el.querySelector('th.hoje')?.textContent?.trim()).toBe('MAR');
  });

  it('US1.2 + FR-008: digitar numa célula vazia salva com Enter e atualiza o total na hora', async () => {
    const { el, limpo, abrirCelula, digitar, celulaDe, atualizar } = await abrir();
    const campo = await abrirCelula('expense:natal', '2027-12');
    digitar(campo, '1200');

    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      kind: 'expense',
      description: 'Natal',
      month: '2027-12',
      value: '1200',
    });
    req.flush({ row: linha('expense', 'Natal', { 11: 80000, 12: 120000 }), totals: totais(30000) });
    await atualizar();
    expect(limpo(celulaDe('expense:natal', '2027-12'))).toContain('1.200,00');
    expect(
      limpo(el.querySelector('[data-testid="total-resultado"] [data-mes="2027-12"]')),
    ).toContain('300,00');
  });

  it('Enter salva e abre a célula de baixo; Esc cancela sem chamar a API', async () => {
    const { abrirCelula, digitar, celulaDe, atualizar } = await abrir();
    const campo = await abrirCelula('expense:natal', '2027-12');
    digitar(campo, '1200');
    // Natal → Tim (mesma coluna): dezembro de Tim está vazio e editável
    expect(celulaDe('expense:tim', '2027-12').querySelector('input')).not.toBeNull();
    http.expectOne('/api/v1/years/2027/planning/cells').flush({
      row: linha('expense', 'Natal', { 11: 80000, 12: 120000 }),
      totals: totais(),
    });
    await atualizar();

    const outro = celulaDe('expense:tim', '2027-12').querySelector('input') as HTMLInputElement;
    digitar(outro, '99', 'Escape');
    expect(celulaDe('expense:tim', '2027-12').querySelector('input')).toBeNull();
    http.expectNone('/api/v1/years/2027/planning/cells');
  });

  it('Tab salva e vai para o mês seguinte', async () => {
    const { abrirCelula, digitar, celulaDe, atualizar } = await abrir();
    const campo = await abrirCelula('expense:natal', '2027-05');
    digitar(campo, '10', 'Tab');
    expect(celulaDe('expense:natal', '2027-06').querySelector('input')).not.toBeNull();
    http
      .expectOne('/api/v1/years/2027/planning/cells')
      .flush({ row: linha('expense', 'Natal', { 5: 1000, 11: 80000 }), totals: totais() });
    await atualizar();
  });

  it('US1.3: alterar o valor de uma célula com um lançamento envia o novo valor', async () => {
    const { abrirCelula, digitar, atualizar } = await abrir();
    const campo = await abrirCelula('expense:tim', '2027-01');
    expect(campo.value).toBe('44,99');
    digitar(campo, '50,99');
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body).toMatchObject({
      description: 'Tim',
      month: '2027-01',
      value: '50,99',
    });
    req.flush({ row: linha('expense', 'Tim', { 1: 5099 }), totals: totais() });
    await atualizar();
  });

  it('US1.4: apagar a célula exclui e oferece "Desfazer"', async () => {
    const { abrirCelula, digitar, el, atualizar } = await abrir();
    const campo = await abrirCelula('expense:tim', '2027-01');
    digitar(campo, '');
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body).toMatchObject({ description: 'Tim', month: '2027-01', value: '' });
    req.flush({
      row: linha('expense', 'Tim', { 2: 4499 }),
      totals: totais(),
      deletionBatchId: 'lote-1',
    });
    await atualizar();
    expect(document.body.textContent).toContain('Excluído');
    expect(el.querySelector('[data-testid="erro-planejamento"]')).toBeNull();
  });

  it('US1.6: aceita "=1100-500" (o servidor interpreta; a tela só valida)', async () => {
    const { abrirCelula, digitar, atualizar } = await abrir();
    const campo = await abrirCelula('expense:natal', '2027-03');
    digitar(campo, '=1100-500');
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body.value).toBe('=1100-500');
    req.flush({ row: linha('expense', 'Natal', { 3: 60000, 11: 80000 }), totals: totais() });
    await atualizar();
  });

  it('valor inválido não chama a API e mostra o motivo', async () => {
    const { abrirCelula, digitar, el, limpo } = await abrir();
    const campo = await abrirCelula('expense:natal', '2027-03');
    digitar(campo, '10/0');
    http.expectNone('/api/v1/years/2027/planning/cells');
    expect(limpo(el.querySelector('[data-testid="erro-planejamento"]'))).toContain(
      'Divisão por zero',
    );
  });

  it('célula vazia que continua vazia não chama a API', async () => {
    const { abrirCelula, digitar } = await abrir();
    const campo = await abrirCelula('expense:natal', '2027-03');
    digitar(campo, '');
    http.expectNone('/api/v1/years/2027/planning/cells');
  });

  it('US1.5: célula com 2+ lançamentos abre a lista e não altera nada', async () => {
    const { fixture, celulaDe, atualizar } = await abrir();
    (celulaDe('expense:tim', '2027-03').querySelector('button.valor') as HTMLButtonElement).click();
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body).toMatchObject({
      description: 'Tim',
      month: '2027-03',
      value: '99,98',
    });
    req.flush(
      {
        code: 'planning.cell_not_editable',
        detail: 'Esta célula tem mais de um lançamento ou algum já efetivado.',
        items: [
          {
            id: 'e-1',
            description: 'Tim',
            amountCents: 4499,
            date: '2027-03-10',
            status: 'planned',
            walletId: 'w-azul',
          },
          {
            id: 'e-2',
            description: 'Tim extra',
            amountCents: 5499,
            date: '2027-03-12',
            status: 'settled',
            walletId: 'w-azul',
          },
        ],
      },
      { status: 409, statusText: 'Conflict' },
    );
    await atualizar();
    fixture.detectChanges();
    const itens = document.querySelectorAll('[data-testid="item-celula"]');
    expect(itens).toHaveLength(2);
    expect(itens[0].textContent).toContain('44,99');
    expect(itens[1].textContent).toContain('Efetivado');
  });

  it('célula editável de um item efetivado aparece marcada e abre a lista (não o campo)', async () => {
    const { celulaDe } = await abrir();
    const abril = celulaDe('expense:tim', '2027-04');
    expect(abril.classList.contains('efetivado')).toBe(true);
    expect(abril.classList.contains('lista')).toBe(true);
    (abril.querySelector('button.valor') as HTMLButtonElement).click();
    http
      .expectOne('/api/v1/years/2027/planning/cells')
      .flush(
        { code: 'planning.cell_not_editable', items: [] },
        { status: 409, statusText: 'Conflict' },
      );
  });

  it('US2.4: digitar na linha do cartão define a estimativa e mostra "est."', async () => {
    const { abrirCelula, digitar, celulaDe, limpo, atualizar } = await abrir();
    const campo = await abrirCelula('invoice:c-azul', '2027-05');
    expect(campo.value).toBe('');
    digitar(campo, '800');
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body).toEqual({
      kind: 'invoice',
      creditCardId: 'c-azul',
      month: '2027-05',
      value: '800',
    });
    req.flush({ row: faturaAzul({ 5: 80000 }), totals: totais() });
    await atualizar();
    const texto = limpo(celulaDe('invoice:c-azul', '2027-05'));
    expect(texto).toContain('800,00');
    expect(texto).toContain('est.');
  });

  it('US1.7: "Nova linha" cria a linha vazia com carteira e dia, que vão na primeira edição', async () => {
    const { el, limpo, abrirCelula, digitar, celulaDe, atualizar, aguardarFechar } = await abrir();
    (el.querySelector('[data-testid="nova-linha"]') as HTMLButtonElement).click();
    await atualizar();
    const noDialogo = (id: string) =>
      document.querySelector(`[data-testid="${id}"]`) as HTMLElement;
    const descricao = noDialogo('nova-linha-descricao') as HTMLInputElement;
    descricao.value = '13º salário';
    descricao.dispatchEvent(new Event('input'));
    noDialogo('nova-linha-entrada').click();
    const carteiraSel = noDialogo('nova-linha-carteira') as HTMLSelectElement;
    carteiraSel.value = 'w-verde';
    carteiraSel.dispatchEvent(new Event('change'));
    const dia = noDialogo('nova-linha-dia') as HTMLInputElement;
    dia.value = '20';
    dia.dispatchEvent(new Event('input'));
    await atualizar();
    noDialogo('nova-linha-criar').click();
    await aguardarFechar();

    const nomes = Array.from(
      el.querySelectorAll('[data-testid="bloco-entradas"] [data-testid="linha-nome"]'),
    ).map((n) => limpo(n));
    expect(nomes).toEqual(['13º salário', 'Salário']);

    const campo = await abrirCelula('income:13º salario', '2027-12');
    digitar(campo, '1500');
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body).toEqual({
      kind: 'income',
      description: '13º salário',
      month: '2027-12',
      value: '1500',
      walletId: 'w-verde',
      day: 20,
    });
    req.flush({ row: linha('income', '13º salário', { 12: 150000 }), totals: totais(150000) });
    await atualizar();
    expect(limpo(celulaDe('income:13º salario', '2027-12'))).toContain('1.500,00');
  });

  it('"Nova linha" de Saída não aparece no bloco de Entradas', async () => {
    const { fixture, el, atualizar, aguardarFechar } = await abrir();
    (el.querySelector('[data-testid="nova-linha"]') as HTMLButtonElement).click();
    await atualizar();
    const descricao = document.querySelector(
      '[data-testid="nova-linha-descricao"]',
    ) as HTMLInputElement;
    descricao.value = 'Mercado';
    descricao.dispatchEvent(new Event('input'));
    await atualizar();
    (document.querySelector('[data-testid="nova-linha-criar"]') as HTMLElement).click();
    await aguardarFechar();
    fixture.detectChanges();
    expect(
      el.querySelectorAll('[data-testid="bloco-entradas"] [data-testid="linha"]'),
    ).toHaveLength(1);
    expect(el.querySelectorAll('[data-testid="bloco-saidas"] [data-testid="linha"]')).toHaveLength(
      3,
    );
  });

  it('linha de "Nova linha" ainda vazia permanece depois de recarregar a grade', async () => {
    const { fixture, el, atualizar, aguardarFechar } = await abrir();
    (el.querySelector('[data-testid="nova-linha"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const descricao = document.querySelector(
      '[data-testid="nova-linha-descricao"]',
    ) as HTMLInputElement;
    descricao.value = 'IPVA';
    descricao.dispatchEvent(new Event('input'));
    await atualizar();
    (document.querySelector('[data-testid="nova-linha-criar"]') as HTMLElement).click();
    await aguardarFechar();
    expect(el.querySelectorAll('[data-testid="bloco-saidas"] [data-testid="linha"]')).toHaveLength(
      3,
    );
  });

  it('US1.8: "Preencher meses" envia o valor e os meses e avisa quantos foram pulados', async () => {
    const { el, limpo, atualizar, aguardarFechar } = await abrir();
    const menu = el.querySelector(
      '[data-linha="expense:natal"] [data-testid="linha-menu"]',
    ) as HTMLButtonElement;
    menu.click();
    await atualizar();
    (document.querySelector('[data-testid="menu-preencher"]') as HTMLElement).click();
    await atualizar();

    const valor = document.querySelector('app-amount-input input') as HTMLInputElement;
    valor.value = '300';
    valor.dispatchEvent(new Event('input'));
    await atualizar();
    // desmarca setembro
    (
      document.querySelector('input[type="checkbox"][data-mes="2027-09"]') as HTMLInputElement
    ).click();
    await atualizar();
    (document.querySelector('[data-testid="preencher-confirmar"]') as HTMLElement).click();
    await aguardarFechar();

    const req = http.expectOne('/api/v1/years/2027/planning/fill');
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(req.request.body.kind).toBe('expense');
    expect(req.request.body.description).toBe('Natal');
    expect(req.request.body.amount).toBe('300');
    expect(req.request.body.months).toHaveLength(11);
    expect(req.request.body.months).not.toContain('2027-09');
    req.flush({
      created: 10,
      skipped: 1,
      row: linha('expense', 'Natal', { 1: 30000, 11: 80000 }),
      totals: totais(),
    });
    await atualizar();
    expect(limpo(el.querySelector('[data-testid="aviso-planejamento"]'))).toBe(
      '10 meses preenchidos; 1 mês já tinha valor e foi pulado.',
    );
  });

  it('US1.9: "Transformar em recorrência" abre o cadastro com o valor mais frequente', async () => {
    const { fixture, el } = await abrir();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (
      el.querySelector('[data-linha="expense:tim"] [data-testid="linha-menu"]') as HTMLElement
    ).click();
    fixture.detectChanges();
    (document.querySelector('[data-testid="menu-recorrencia"]') as HTMLElement).click();
    expect(navegar).toHaveBeenCalledWith(['/recorrencias/nova'], {
      queryParams: {
        kind: 'expense',
        description: 'Tim',
        dayOfMonth: '10',
        startMonth: '2027-01',
        amount: '44,99',
        walletId: 'w-azul',
      },
    });
  });

  it('celular: toque na célula abre o diálogo de edição e salva o valor', async () => {
    const { abrirCelula, el, atualizar, aguardarFechar } = await abrir(grade2027(), true);
    expect(el.querySelector('[data-testid="linha-menu"]')).not.toBeNull();
    await abrirCelula('expense:natal', '2027-12');
    await atualizar();
    expect(el.querySelector('[data-testid="celula-campo"]')).toBeNull();
    const campo = document.querySelector('app-celula-dialog input') as HTMLInputElement;
    campo.value = '1200';
    campo.dispatchEvent(new Event('input'));
    await atualizar();
    (document.querySelector('[data-testid="celula-salvar"]') as HTMLElement).click();
    await aguardarFechar();
    const req = http.expectOne('/api/v1/years/2027/planning/cells');
    expect(req.request.body).toMatchObject({
      description: 'Natal',
      month: '2027-12',
      value: '1200',
    });
    req.flush({ row: linha('expense', 'Natal', { 11: 80000, 12: 120000 }), totals: totais(30000) });
    await atualizar();
  });

  it('celular: "Preencher meses" fica escondido do menu da linha', async () => {
    const { fixture, el } = await abrir(grade2027(), true);
    (
      el.querySelector('[data-linha="expense:tim"] [data-testid="linha-menu"]') as HTMLElement
    ).click();
    fixture.detectChanges();
    expect(document.querySelector('[data-testid="menu-preencher"]')).toBeNull();
    expect(document.querySelector('[data-testid="menu-recorrencia"]')).not.toBeNull();
    // a linha de fatura não tem nenhum item de menu no celular
    expect(el.querySelector('[data-linha="invoice:c-azul"] [data-testid="linha-menu"]')).toBeNull();
  });

  it('trocar o ano navega para /planejamento/:ano', async () => {
    const { el } = await abrir();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (el.querySelector('[data-testid="planejamento-seguinte"]') as HTMLElement).click();
    expect(navegar).toHaveBeenCalledWith(['/planejamento', 2028]);
  });
});
