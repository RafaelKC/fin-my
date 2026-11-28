/**
 * Bancos com logo (SVG em `public/bancos/<código>.svg`, de github.com/rzmt/logos-bancos-br; marcas dos titulares, só para
 * identificar a instituição) e a cor da marca, usada como cor da carteira/cartão. Código = COMPE.
 */
export interface Banco {
  codigo: string;
  nome: string;
  cor: string;
}

export const BANCOS: readonly Banco[] = [
  { codigo: '077', nome: 'Inter', cor: '#FF7A00' },
  { codigo: '380', nome: 'PicPay', cor: '#11C76F' },
  { codigo: '748', nome: 'Sicredi', cor: '#3FA110' },
  { codigo: '260', nome: 'Nubank', cor: '#820AD1' },
  { codigo: '341', nome: 'Itaú', cor: '#EC7000' },
  { codigo: '237', nome: 'Bradesco', cor: '#CC092F' },
  { codigo: '001', nome: 'Banco do Brasil', cor: '#F8D117' },
  { codigo: '104', nome: 'Caixa', cor: '#005CA9' },
  { codigo: '033', nome: 'Santander', cor: '#EC0000' },
  { codigo: '336', nome: 'C6 Bank', cor: '#242424' },
  { codigo: '323', nome: 'Mercado Pago', cor: '#00B1EA' },
  { codigo: '290', nome: 'PagBank', cor: '#1BB99A' },
  { codigo: '756', nome: 'Sicoob', cor: '#003641' },
  { codigo: '208', nome: 'BTG Pactual', cor: '#0B2A4A' },
  { codigo: '212', nome: 'Original', cor: '#00A857' },
  { codigo: '041', nome: 'Banrisul', cor: '#0047BB' },
  { codigo: '102', nome: 'XP', cor: '#111111' },
  { codigo: '403', nome: 'Cora', cor: '#FE3E6D' },
  { codigo: '422', nome: 'Safra', cor: '#1C2A4A' },
];

export function bancoPorCodigo(codigo: string | null | undefined): Banco | null {
  return BANCOS.find((b) => b.codigo === codigo) ?? null;
}

export function logoDoBanco(codigo: string): string {
  return `bancos/${codigo}.svg`;
}
