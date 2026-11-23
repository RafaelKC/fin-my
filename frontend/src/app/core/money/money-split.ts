/**
 * Divide `totalCents` em `parts` partes (RT-02): cada parte recebe o piso de `total / partes` e a
 * sobra de centavos vai para a primeira. Ex.: 10000 em 3 → [3334, 3333, 3333].
 */
export function splitMoney(totalCents: number, parts: number): number[] {
  if (!Number.isSafeInteger(totalCents) || totalCents < 0) {
    throw new RangeError('totalCents deve ser um inteiro não negativo.');
  }

  if (!Number.isSafeInteger(parts) || parts < 1) {
    throw new RangeError('parts deve ser um inteiro maior ou igual a 1.');
  }

  const share = Math.floor(totalCents / parts);
  const result = new Array<number>(parts).fill(share);
  result[0] += totalCents - share * parts;
  return result;
}
