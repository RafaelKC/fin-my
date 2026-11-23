import { Entry } from '../../lancamento/entry.models';
import { mensagemExclusao, rotuloRealizar, tituloAviso } from './pendencias.logic';

const e = (kind: Entry['kind']) => ({ kind }) as Entry;

describe('pendencias.logic', () => {
  it('tituloAviso concorda no singular e no plural', () => {
    expect(tituloAviso(1)).toBe('1 pendência de meses anteriores');
    expect(tituloAviso(3)).toBe('3 pendências de meses anteriores');
  });

  it('rotuloRealizar segue o tipo dos selecionados', () => {
    expect(rotuloRealizar([e('income')])).toBe('Marcar como recebida');
    expect(rotuloRealizar([e('expense'), e('expense')])).toBe('Marcar como paga');
    expect(rotuloRealizar([e('income'), e('expense')])).toBe('Marcar como recebida/paga');
    expect(rotuloRealizar([])).toBe('Marcar como recebida/paga');
  });

  it('mensagemExclusao', () => {
    expect(mensagemExclusao(1)).toBe('Lançamento excluído.');
    expect(mensagemExclusao(2)).toBe('2 lançamentos excluídos.');
  });
});
