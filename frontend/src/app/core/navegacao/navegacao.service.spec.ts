import { TestBed } from '@angular/core/testing';
import { MAX_NA_BARRA, NavegacaoService, normalizar } from './navegacao.service';

describe('NavegacaoService', () => {
  beforeEach(() => localStorage.removeItem('finmy.navegacao'));

  const ids = (itens: { id: string }[]) => itens.map((i) => i.id);

  it('padrão: Mês e Cartões antes do "+", Acertos depois e o resto no "Mais" com o Perfil no fim', () => {
    const nav = TestBed.inject(NavegacaoService);
    expect(ids(nav.esquerda())).toEqual(['mes', 'cartoes']);
    expect(ids(nav.direita())).toEqual(['acertos']);
    expect(ids(nav.mais())).toEqual([
      'painel',
      'planejamento',
      'carteiras',
      'recorrencias',
      'busca',
      'tags',
      'sugestoes',
      'perfil',
    ]);
  });

  it('põe na barra até 4, reordena e lembra no aparelho', () => {
    const nav = TestBed.inject(NavegacaoService);
    expect(nav.alternarBarra('painel')).toBe(true);
    expect(nav.alternarBarra('busca')).toBe(false); // já são 4
    expect(nav.config().naBarra).toHaveLength(MAX_NA_BARRA);

    nav.mover('painel', -1); // painel sobe acima de acertos
    expect(ids(nav.direita())).toEqual(['painel', 'acertos']);

    nav.alternarBarra('cartoes'); // tira da barra: vai para o "Mais" na posição da ordem
    expect(ids(nav.esquerda())).toEqual(['mes', 'painel']);
    expect(ids(nav.mais())[0]).toBe('cartoes');

    const salvo = JSON.parse(localStorage.getItem('finmy.navegacao')!);
    expect(salvo.naBarra).toEqual(['mes', 'painel', 'acertos']);
  });

  it('restaurar volta ao padrão; configuração estranha é normalizada', () => {
    const nav = TestBed.inject(NavegacaoService);
    nav.alternarBarra('mes');
    nav.restaurar();
    expect(ids(nav.esquerda())).toEqual(['mes', 'cartoes']);

    const n = normalizar({
      ordem: ['tags', 'xyz', 'tags'],
      naBarra: ['tags', 'mes', 'busca', 'painel', 'cartoes', 'xyz'],
    });
    expect(n.ordem[0]).toBe('tags');
    expect(n.ordem).toHaveLength(10);
    expect(n.naBarra).toHaveLength(4);
    expect(n.naBarra).not.toContain('xyz');
  });
});
