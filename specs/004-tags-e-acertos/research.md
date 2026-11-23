# Research — 004 Tags e acerto de contas

Normas: `docs/arquitetura.md`, `docs/modelo-de-dados.md` (`tags`, `entry_tags`, `card_purchase_tags`,
`settlement_tag_id`, `split_group_id`); decisões da 002/003 continuam valendo.

## R1. Tags na API dos lançamentos

- Entradas, Saídas e Compras recebem `tagIds: uuid[]` (todas as tags, de acerto ou não; máx. 20). O
  servidor separa: a tag com `is_settlement = true` vai para `settlement_tag_id`, as outras para a
  tabela de ligação. Mais de uma de acerto → 422 `tags.multiple_settlement` ("Um lançamento só pode
  ter uma tag de acerto. Use Dividir."). Tag arquivada só pode ser **mantida** (já estava no
  lançamento), nunca adicionada → 422 `tag.archived`. Tag de outro Espaço → 404.
- Respostas trazem `tagIds` (todas) e `settlementTagId`.
- Em parcela, `PUT /card-purchases/{id}` aplica `tagIds` conforme `scope` (`this | all`).
- Sugestões (`GET /entries/suggestions`) devolvem também `tagIds` do lançamento mais recente com a
  mesma descrição normalizada e o mesmo tipo (tags arquivadas omitidas).

## R2. Criação inline e ordenação

- `POST /tags { name, color?, isSettlement? }`; nome já existente (normalizado) → 409
  `tag.name_taken` com extensão `existingTagId` — o front usa a existente (US1.3). Cor padrão: próxima
  da paleta de 10 cores.
- `GET /tags` traz `uses90d` = nº de entries com `entry_date ≥ hoje − 90 dias` + compras com
  `purchase_date ≥ hoje − 90 dias` que têm a tag (coluna ou ligação). O front ordena por `uses90d` desc,
  depois nome; mostra 8 como botões.

## R3. Acertos (FR-006..FR-009)

- `SettlementCalculator` (Domain, puro) recebe, por tag e mês ≤ M: Σ saídas, Σ débitos − créditos de
  compras por `invoices.due_month`, Σ entradas, Σ entradas recebidas; devolve Gasto, Cobrado, Recebido,
  A cobrar, A receber, Saldo acumulado. Testado com o exemplo da US2 (SC-002) e US2.2.
- `GET /settlements/{month}` → uma linha por tag de acerto ativa + arquivadas com algum valor ≠ 0 no
  mês. `GET /settlements/{month}/tags/{tagId}/items` → lançamentos que compõem os números
  (`group: spent | charged`, com `received: bool` nas entradas).

## R4. Gerar cobrança (FR-010)

- Só no front: abre o formulário rápido de Entrada pré-preenchido (descrição `"<Tag> — acerto MM/AAAA"`,
  valor = A cobrar, Mês de referência M, data = hoje se M é o mês de hoje, senão dia 1 de M; carteira
  principal; Prevista; tag). Nada é salvo sem "Salvar". Após salvar, recarrega a tela.

## R5. Divisão (FR-012..FR-014)

- Endpoints próprios: `POST /entries/split` (só `kind = expense`) e `POST /card-purchases/split`. Corpo =
  o mesmo da criação + `split { mode: equal | amounts, parts: [{ settlementTagId: uuid|null, amount?: string }] }`,
  2–10 partes, `settlementTagId` sem repetir (`null` = "Eu", no máximo uma vez). `tagIds` do corpo
  não podem conter tag de acerto (cada parte define a sua). `amounts`: soma ≠ total → 422
  `split.sum_mismatch` com `differenceCents`. `equal`: `MoneySplitter` na ordem das partes.
- Compra parcelada dividida: cada parte vira um `installment_plan` próprio com o valor da parte como
  valor total (modo `total_amount`); se a pessoa usou "valor da parcela", o total = valor × N antes de
  dividir. Compra de crédito não pode ser dividida (422 `split.credit_not_allowed`).
- Todas as partes recebem o mesmo `split_group_id`; respostas trazem `splitGroupId` e `splitCount`.
  Fora isso as partes são independentes (editar/excluir uma não toca as outras). Idempotência vale.

## R6. Filtros por tag (FR-004)

- `GET /months/{m}?tagIds=` filtra as listas: entries com qualquer das tags; e acrescenta
  `purchases[]` = compras das faturas com vencimento em M que têm qualquer das tags. Com filtro de tag
  a lista de faturas e transferências fica vazia. `filterTotals { incomeCents, expenseCents,
  purchasesNetCents }` somam os itens listados. O resumo (Previsto/Realizado) não muda com filtro de tag.
- `GET /credit-cards/{id}/invoices/{m}?tagIds=` filtra `purchases` e devolve `filteredTotalCents`.
  `GET /entries/search?tagIds=` idem (compras passam a entrar na busca com `type = card_purchase`).
- Fatura sempre devolve `settlementSubtotals [{ tagId|null, cents }]` (FR-011; SC-004 testado).

## R7. Gerenciar tags (FR-005)

- `PUT /tags/{id}` (nome, cor, `isSettlement`, `version`); `archive`/`unarchive`; `DELETE` só sem uso
  (inclui recorrências a partir da 005) senão 409 `tag.in_use_archive_instead`;
  `POST /tags/{id}/merge { targetTagId }`.
- Mudar `isSettlement` para true: se algum lançamento já tem outra tag de acerto junto com esta → 409
  `tag.settlement_conflict` com extensão `conflicts: [{ type, id, description, date }]` (US5.2); senão
  move as ligações para `settlement_tag_id` na mesma transação. Para false: move a coluna para a
  ligação.
- Junção: aplica a tag destino onde havia a origem (sem duplicar), valida o conflito de duas de
  acerto (US5.4, mesmo 409) e faz soft delete da origem. A junção não tem Desfazer (o front confirma).
