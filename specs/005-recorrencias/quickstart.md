# Quickstart — validar a 005

| Suíte | Cobre |
|---|---|
| `RecurrenceSchedulerTests` (Domain) | US1.1 (22 ocorrências), US1.4, US1.5, US1.6, US2.1 |
| HTTP `Recurrences/` | US1.1–US1.6, US2.1, US2.2, US3.1–US3.4, US4.1, arquivados (Edge Case) |
| `RecurrenceHorizonJobTests` | US1.2 com relógio fixo em 2027-01-01; idempotência; ocorrência excluída não volta (US3.3) |
| e2e `recorrencias.spec.ts` | criar, alterar a partir de, encerrar, repetir todo mês |

Manual: criar "Tim" 44,99 dia 10 → ver a Saída prevista em todos os meses até dezembro do ano seguinte;
salário com "mês seguinte"; alterar valor a partir de setembro; encerrar WellHub.
