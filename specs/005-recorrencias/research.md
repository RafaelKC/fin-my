# Research — 005 Recorrências

Normas: `docs/modelo-de-dados.md` (`recurrences`, `recurrence_tags`, colunas `recurrence_*` em
`entries`/`card_purchases`); decisões da 002–004.

## R1. Regra pura

`FinMy.Domain/Recurrences/RecurrenceScheduler.Occurrences(rule, fromMonthExclusive, today)` devolve os
meses a gerar: de `max(start_month, generated_until_month + 1)` até `min(dezembro do ano seguinte ao de
hoje, end_month)`; para cada mês, data = `day_of_month` com RT-03; Mês de referência = mês da data ou o
seguinte (`reference_month_rule`); para compra, a fatura vem de `InvoiceCycle.InvoiceFor` (003).
`after_count`: `end_month = start_month + (N − 1)` gravado na criação. Testes com US1.1 (22
ocorrências), US1.4, US1.5, US1.6, US2.1.

## R2. Geração e horizonte (FR-003, FR-008)

- `RecurrenceGenerator` (Application) cria as ocorrências que faltam e avança `generated_until_month`
  na mesma transação. Chamado ao criar/alterar a recorrência e por um job diário Hangfire (03:00
  America/Sao_Paulo) via `ITenantJobRunner` para todo Espaço — a virada do ano estende sozinha.
- Ocorrência excluída não volta porque o gerador só cria meses > `generated_until_month`.
  Índice único parcial `(tenant_id, recurrence_id, recurrence_month) WHERE deleted_at IS NULL` em
  `entries` e em `card_purchases` impede duplicata em corrida.
- Ocorrência de entrada/saída: `status = planned` sempre (US1.6). Compra: à vista, débito,
  `origin = 'recurrence'`.
- Carteira/cartão/tag arquivado (Edge Case): o gerador não cria nada para a recorrência e não avança
  `generated_until_month`; a API devolve `needsReview: true` com `reviewReason`
  (`wallet_archived | card_archived | tag_archived`). Ao desarquivar, a próxima execução cria os meses
  pendentes que ainda estão no horizonte.

## R3. "Alterada" (FR-005)

- `PUT /entries/{id}` e `PUT /card-purchases/{id}` marcam `recurrence_modified = true` quando a
  ocorrência muda qualquer campo além de `status`/`reconciled` (comparar valores normalizados antes e
  depois). `PATCH` de situação e de conferida nunca marcam. Respostas ganham `recurrenceId`,
  `recurrenceModified`.

## R4. Alterar a partir de M (FR-006) e encerrar (FR-007)

- `PUT /recurrences/{id}` com o modelo completo + `fromMonth` (padrão mês de hoje; não pode ser
  anterior ao `start_month`). Atualiza o modelo e aplica descrição, valor, dia, carteira/cartão, tags e
  regra de mês às ocorrências elegíveis: `recurrence_month ≥ M`, não alteradas e (entrada/saída)
  `planned` ou (compra) em fatura com `closing_date > hoje`. Compra com dia novo recalcula a fatura.
  `kind` não pode mudar (422 `recurrence.kind_immutable`). Mudar término: `end_type/end_month` novos;
  se o novo fim encolher, as ocorrências elegíveis depois dele são excluídas (como encerrar).
- `POST /recurrences/{id}/end { fromMonth }`: `end_type = until_month`, `end_month = M − 1`, exclui
  (soft delete, um lote) as elegíveis ≥ M. Devolve `deletionBatchId`, mas **não** há "Desfazer" na
  interface (o front confirma antes); restaurar o lote volta só os lançamentos.
- `DELETE /recurrences/{id}`: encerra a partir do mês de hoje, zera `recurrence_id/recurrence_month`
  das ocorrências que sobraram (viram lançamentos comuns) e faz soft delete da recorrência.

## R5. Repetir todo mês (FR-010, US4)

- `POST /recurrences/from-item { type: entry|card_purchase, id, endType?, endMonth?, occurrenceLimit? }`:
  cria a recorrência com descrição, valor, carteira/cartão e tags do lançamento; `day_of_month` = dia da
  data; `start_month` = mês da data; regra de mês = `next_month` se o Mês de referência do lançamento
  for o mês seguinte ao da data, senão `same_month`. O lançamento vira a ocorrência daquele mês
  (`recurrence_month` = mês da data, não alterado); gera as seguintes. Compra parcelada ou de crédito,
  ou lançamento que já é ocorrência → 422 `recurrence.item_not_eligible`.

## R6. Tela (FR-009)

- `GET /recurrences?status=active|ended|all` devolve `nextOccurrenceDate` (menor data ≥ hoje entre as
  ocorrências não excluídas), `needsReview`, e o resumo `monthlyIncomeCents`/`monthlyExpenseCents`
  (soma das ativas; compras entram em despesa).
