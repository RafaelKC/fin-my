import { Pipe, PipeTransform } from '@angular/core';

/** Formata centavos em reais: `123456` → `R$ 1.234,56`; negativo → `-R$ 1.234,56`. */
export function formatBrl(cents: number): string {
  const negative = cents < 0;
  const absolute = Math.abs(cents);
  const reais = Math.floor(absolute / 100)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  const centavos = (absolute % 100).toString().padStart(2, '0');
  return `${negative ? '-' : ''}R$ ${reais},${centavos}`;
}

/** `{{ valorEmCentavos | brl }}`; vazio quando não há valor. */
@Pipe({ name: 'brl' })
export class BrlPipe implements PipeTransform {
  transform(cents: number | null | undefined): string {
    return cents === null || cents === undefined || !Number.isSafeInteger(cents)
      ? ''
      : formatBrl(cents);
  }
}
