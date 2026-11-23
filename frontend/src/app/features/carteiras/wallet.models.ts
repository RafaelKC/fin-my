export type WalletType = 'checking' | 'digital' | 'savings' | 'cash' | 'meal_voucher' | 'other';

/** `Wallet` do contrato (contracts/openapi.yaml). */
export interface Wallet {
  id: string;
  name: string;
  type: WalletType;
  color: string;
  initialBalanceCents: number;
  initialBalanceDate: string;
  isPrimary: boolean;
  archived: boolean;
  currentBalanceCents: number;
  hasEntries: boolean;
  version: number;
}

/** `WalletInput` do contrato; `initialBalance` é uma expressão (RT-05) enviada como digitada. */
export interface WalletInput {
  name: string;
  type: WalletType;
  color: string;
  initialBalance: string;
  initialBalanceDate: string;
}

export const WALLET_TYPES: readonly { value: WalletType; label: string }[] = [
  { value: 'checking', label: 'Conta corrente' },
  { value: 'digital', label: 'Conta digital' },
  { value: 'savings', label: 'Poupança' },
  { value: 'cash', label: 'Dinheiro' },
  { value: 'meal_voucher', label: 'Vale-refeição/alimentação' },
  { value: 'other', label: 'Outro' },
];

/** Paleta fixa de 10 cores (FR-001). */
export const WALLET_COLORS: readonly { value: string; label: string }[] = [
  { value: '#1E88E5', label: 'Azul' },
  { value: '#43A047', label: 'Verde' },
  { value: '#E53935', label: 'Vermelho' },
  { value: '#FB8C00', label: 'Laranja' },
  { value: '#FDD835', label: 'Amarelo' },
  { value: '#8E24AA', label: 'Roxo' },
  { value: '#D81B60', label: 'Rosa' },
  { value: '#00ACC1', label: 'Turquesa' },
  { value: '#6D4C41', label: 'Marrom' },
  { value: '#546E7A', label: 'Cinza' },
];

export const NOME_CARTEIRA_MAX = 40;
