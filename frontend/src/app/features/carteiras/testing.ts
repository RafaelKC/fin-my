import { Wallet } from './wallet.models';

export function carteira(parcial: Partial<Wallet> = {}): Wallet {
  return {
    id: 'w-azul',
    name: 'Azul',
    type: 'checking',
    color: '#1E88E5',
    initialBalanceCents: 50000,
    initialBalanceDate: '2026-03-01',
    isPrimary: true,
    archived: false,
    currentBalanceCents: 50000,
    hasEntries: false,
    version: 1,
    ...parcial,
  };
}
