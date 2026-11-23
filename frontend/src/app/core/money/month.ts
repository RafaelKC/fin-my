/** Mês de referência no formato `AAAA-MM`. */
export type YearMonth = string;

const NAMES = [
  'Janeiro',
  'Fevereiro',
  'Março',
  'Abril',
  'Maio',
  'Junho',
  'Julho',
  'Agosto',
  'Setembro',
  'Outubro',
  'Novembro',
  'Dezembro',
];

const MONTH_PATTERN = /^(\d{4})-(0[1-9]|1[0-2])$/;

export function isYearMonth(value: string): boolean {
  return MONTH_PATTERN.test(value);
}

function split(month: YearMonth): { year: number; month: number } {
  const match = MONTH_PATTERN.exec(month);
  if (!match) {
    throw new RangeError(`Mês inválido: "${month}". Use AAAA-MM.`);
  }

  return { year: Number(match[1]), month: Number(match[2]) };
}

function join(year: number, month: number): YearMonth {
  return `${year.toString().padStart(4, '0')}-${month.toString().padStart(2, '0')}`;
}

/** `2026-03` → `Março de 2026`. */
export function monthLabel(month: YearMonth): string {
  const { year, month: m } = split(month);
  return `${NAMES[m - 1]} de ${year}`;
}

/** `2026-03` → `MAR/26`. */
export function monthShortLabel(month: YearMonth): string {
  const { year, month: m } = split(month);
  return `${NAMES[m - 1].slice(0, 3).toUpperCase()}/${(year % 100).toString().padStart(2, '0')}`;
}

/** Mês anterior: `2026-01` → `2025-12`. */
export function previousMonth(month: YearMonth): YearMonth {
  const { year, month: m } = split(month);
  return m === 1 ? join(year - 1, 12) : join(year, m - 1);
}

/** Mês seguinte: `2025-12` → `2026-01`. */
export function nextMonth(month: YearMonth): YearMonth {
  const { year, month: m } = split(month);
  return m === 12 ? join(year + 1, 1) : join(year, m + 1);
}

/** Mês de uma data `AAAA-MM-DD`: `2026-03-15` → `2026-03`. */
export function monthOfDate(date: string): YearMonth {
  const match = /^(\d{4}-(?:0[1-9]|1[0-2]))-(0[1-9]|[12]\d|3[01])$/.exec(date);
  if (!match) {
    throw new RangeError(`Data inválida: "${date}". Use AAAA-MM-DD.`);
  }

  return match[1];
}

/** Soma `count` meses (pode ser negativo): `2026-11` + 3 → `2027-02`. */
export function addMonths(month: YearMonth, count: number): YearMonth {
  const { year, month: m } = split(month);
  const index = year * 12 + (m - 1) + count;
  return join(Math.floor(index / 12), (index % 12) + 1);
}
