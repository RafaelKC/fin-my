# Contrato — 007 (REST, convenções da 002–006)

## Tipos

```text
PlanningCell   { month: "AAAA-MM", plannedCents, itemCount, allSettled, editable, entryId?: uuid }
PlanningRow    { kind: income|expense, key, description, latestWalletId?, latestDay?, latestTagIds[], cells: PlanningCell[12] }
InvoiceCell    { month, plannedCents, totalCents, estimateCents?, editable: true }
InvoiceRow     { creditCardId, creditCardName, archived, cells: InvoiceCell[12] }
PlanningTotals { months: [{ month, incomeCents, expenseCents, resultCents, finalBalanceCents }] (Previsto) }
```

## Endpoints

| Método e rota | Corpo | Resposta | Erros |
|---|---|---|---|
| `GET /years/{yyyy}/planning` | — | `{ year, today, incomeRows, expenseRows, invoiceRows, totals }` | 422 ano fora de 2000–2100 |
| `PUT /years/{yyyy}/planning/cells` | research R3 | `{ row: PlanningRow \| InvoiceRow, totals, deletionBatchId? }` | 409 `planning.cell_not_editable` (extensão `items`), 422 validação/valor |
| `POST /years/{yyyy}/planning/fill` | research R3 | `{ created, skipped, row, totals }` | 422 |
| `GET /years/{yyyy}/planning/copy-candidates` | — | `{ rows: [{ key, kind, description, settlementTagName?, cells }], estimates: [{ creditCardId, creditCardName, months: [{month, estimateCents}] }] }` | |
| `POST /years/{yyyy}/planning/copy` | `{ rowKeys[], copyEstimates }` | `{ created, skipped }` | 422 |
| `PUT /credit-cards/{id}/invoices/{month}/estimate` | `{ amount: string \| null }` | `Invoice` (003) com `estimateCents`, `estimateActive` | 422 |

Alterações: `Invoice` e a linha de fatura de `GET /months/{m}` ganham `estimateCents` e
`estimateActive`; `plannedCents` passa a usar `InvoiceForecast`.

## Telas

| Rota | Tela |
|---|---|
| `/planejamento/:ano` (`/planejamento` → ano de hoje) | research R5 |
| `/cartoes/:id/faturas/:mes` (alteração) | campo "Estimativa" (definir/remover) e texto "Estimado R$ X" |
| `/mes/:mes` (alteração) | linha da fatura "Previsto R$ 800,00 — lançado R$ 205,53" quando a estimativa vale |

"Planejamento" entra no menu lateral e em "Mais".
