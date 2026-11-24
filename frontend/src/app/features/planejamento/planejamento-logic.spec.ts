import { PlanningCell } from './planning.models';
import {
  aplicarLinha,
  chaveDaLinha,
  linhaVazia,
  mesclar,
  mesesDoAno,
  primeiroMesComValor,
  valorMaisFrequente,
  vizinha,
} from './planejamento-logic';

const celula = (month: string, cents: number): PlanningCell => ({
  month,
  plannedCents: cents,
  itemCount: cents > 0 ? 1 : 0,
  allSettled: false,
  editable: true,
});

describe('planejamento-logic', () => {
  it('monta a chave como o servidor: tipo + descrição sem acento e em minúsculas', () => {
    expect(chaveDaLinha('income', '  13º Salário ')).toBe('income:13º salario');
    expect(chaveDaLinha('expense', 'NATAL')).toBe('expense:natal');
  });

  it('lista os 12 meses do ano', () => {
    const meses = mesesDoAno(2027);
    expect(meses).toHaveLength(12);
    expect(meses[0]).toBe('2027-01');
    expect(meses[11]).toBe('2027-12');
  });

  it('valor mais frequente: o que mais se repete; empate fica com o maior', () => {
    const cells = [
      celula('2027-01', 4499),
      celula('2027-02', 4499),
      celula('2027-03', 5099),
      celula('2027-04', 0),
    ];
    expect(valorMaisFrequente(cells)).toBe(4499);
    expect(valorMaisFrequente([celula('2027-01', 100), celula('2027-02', 300)])).toBe(300);
    expect(valorMaisFrequente([celula('2027-01', 0)])).toBe(0);
  });

  it('primeiro mês com valor', () => {
    expect(primeiroMesComValor([celula('2027-01', 0), celula('2027-02', 10)])).toBe('2027-02');
    expect(primeiroMesComValor([celula('2027-01', 0)])).toBeNull();
  });

  describe('aplicarLinha e mesclar', () => {
    const natal = linhaVazia(2027, 'expense', 'Natal', null, 1);
    const tim = linhaVazia(2027, 'expense', 'Tim', null, 10);
    natal.cells[11] = celula('2027-12', 120000);
    tim.cells[2] = celula('2027-03', 4499);

    it('insere e troca a linha devolvida pelo servidor', () => {
      const lista = aplicarLinha([tim], natal);
      expect(lista.map((l) => l.description).sort()).toEqual(['Natal', 'Tim']);
      const trocada = { ...tim, description: 'TIM' };
      expect(aplicarLinha(lista, trocada).map((l) => l.description)).toContain('TIM');
      expect(aplicarLinha(lista, trocada)).toHaveLength(2);
    });

    it('linha sem lançamentos sai da lista do servidor', () => {
      const vazia = linhaVazia(2027, 'expense', 'Tim', null, 10);
      expect(aplicarLinha([tim, natal], vazia)).toEqual([natal]);
    });

    it('mescla as novas ainda vazias em ordem alfabética, sem duplicar', () => {
      const bolo = linhaVazia(2027, 'expense', 'Bolo', null, 1);
      expect(mesclar([tim, natal], [bolo]).map((l) => l.description)).toEqual([
        'Bolo',
        'Natal',
        'Tim',
      ]);
      expect(mesclar([tim], [linhaVazia(2027, 'expense', 'tim', null, 1)])).toEqual([tim]);
    });
  });

  describe('vizinha', () => {
    const linhas = ['a', 'b'];
    const meses = ['2027-01', '2027-02'];

    it('Enter desce na mesma coluna; no fim da grade sai', () => {
      expect(vizinha(linhas, meses, { linha: 'a', mes: '2027-02' }, 'baixo')).toEqual({
        linha: 'b',
        mes: '2027-02',
      });
      expect(vizinha(linhas, meses, { linha: 'b', mes: '2027-02' }, 'baixo')).toBeNull();
    });

    it('Tab vai para a direita e quebra para a próxima linha', () => {
      expect(vizinha(linhas, meses, { linha: 'a', mes: '2027-01' }, 'direita')).toEqual({
        linha: 'a',
        mes: '2027-02',
      });
      expect(vizinha(linhas, meses, { linha: 'a', mes: '2027-02' }, 'direita')).toEqual({
        linha: 'b',
        mes: '2027-01',
      });
      expect(vizinha(linhas, meses, { linha: 'b', mes: '2027-02' }, 'direita')).toBeNull();
    });

    it('Shift+Tab e seta para cima fazem o caminho inverso', () => {
      expect(vizinha(linhas, meses, { linha: 'b', mes: '2027-01' }, 'esquerda')).toEqual({
        linha: 'a',
        mes: '2027-02',
      });
      expect(vizinha(linhas, meses, { linha: 'a', mes: '2027-01' }, 'esquerda')).toBeNull();
      expect(vizinha(linhas, meses, { linha: 'b', mes: '2027-01' }, 'cima')).toEqual({
        linha: 'a',
        mes: '2027-01',
      });
    });
  });
});
