# Contrato — 005 (REST, mesmas convenções da 002–004)

## Recurso `Recurrence`

```json
{
  "id": "uuid", "kind": "income|expense|card_purchase",
  "description": "Tim", "amountCents": 4499, "dayOfMonth": 10,
  "walletId": "uuid|null", "creditCardId": "uuid|null", "tagIds": ["uuid"],
  "startMonth": "2026-03", "endType": "none|until_month|after_count",
  "endMonth": "2027-02|null", "occurrenceLimit": 12,
  "referenceMonthRule": "same_month|next_month|null",
  "generatedUntilMonth": "2027-12", "status": "active|ended",
  "nextOccurrenceDate": "2026-03-10|null",
  "needsReview": false, "reviewReason": "wallet_archived|card_archived|tag_archived|null",
  "version": 1
}
```

Entrada (`RecurrenceInput`): `kind`, `description`, `amount` (texto RT-05), `dayOfMonth`, `walletId` ou
`creditCardId`, `tagIds`, `startMonth`, `endType`, `endMonth?`, `occurrenceLimit?`,
`referenceMonthRule?` (padrão `same_month`; proibido em compra).

## Endpoints

| Método e rota | Corpo | Resposta | Erros |
|---|---|---|---|
| `GET /recurrences?status=active\|ended\|all` | — | `{ items: Recurrence[], monthlyIncomeCents, monthlyExpenseCents }` | |
| `POST /recurrences` (Idempotency-Key) | `RecurrenceInput` | 201 `Recurrence` (ocorrências já geradas) | 422 validação, `wallet.archived`, `credit_card.archived` |
| `GET /recurrences/{id}` | — | `Recurrence` | 404 |
| `PUT /recurrences/{id}` | `RecurrenceInput` + `fromMonth` + `version` | `Recurrence` + `affectedCount` | 422 `recurrence.kind_immutable`, `recurrence.from_month_before_start`; 409 `concurrency_conflict` |
| `POST /recurrences/{id}/end` | `{ fromMonth }` | `{ recurrence, deletedCount, deletionBatchId }` | 422 `recurrence.already_ended` |
| `DELETE /recurrences/{id}` | — | `{ deletedCount, unlinkedCount }` | 404 |
| `POST /recurrences/from-item` | `{ type, id, endType?, endMonth?, occurrenceLimit? }` | 201 `Recurrence` | 422 `recurrence.item_not_eligible` |

Alterações em contratos existentes: `Entry` e `CardPurchase` ganham `recurrenceId`, `recurrenceMonth`,
`recurrenceModified`; `card_purchases.origin` ganha `recurrence`.

## Telas

| Rota | Tela |
|---|---|
| `/recorrencias` | Lista (Ativas / Encerradas): descrição, valor, dia, carteira/cartão, próxima ocorrência, aviso "Revise: carteira/cartão arquivado"; topo com total mensal de entradas e de saídas+compras; "Nova recorrência" |
| `/recorrencias/nova`, `/recorrencias/:id` | Formulário (tipo, descrição, valor com calculadora, dia, carteira/cartão, tags, mês inicial, término, regra de mês só para entrada/saída). Ao salvar edição: diálogo "A partir de qual mês?" (padrão mês de hoje). Ações: Encerrar (diálogo "a partir de" + confirmação), Excluir (confirmação) |

Listas do mês, fatura e busca mostram um ícone de recorrência nas ocorrências e "alterada" quando
`recurrenceModified`. O formulário rápido em modo edição de Entrada/Saída/Compra à vista ganha
"Repetir todo mês" (abre término e confirma). "Recorrências" entra em "Mais" e no menu lateral.
