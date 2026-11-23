const TIME_ZONE = 'America/Sao_Paulo';

const formatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/** Data de hoje (`AAAA-MM-DD`) em America/Sao_Paulo, independentemente do fuso do aparelho. */
export function today(now: Date = new Date()): string {
  const parts = Object.fromEntries(formatter.formatToParts(now).map((p) => [p.type, p.value]));
  return `${parts['year']}-${parts['month']}-${parts['day']}`;
}
