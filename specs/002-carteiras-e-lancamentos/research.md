# Research — 002 Carteiras, Entradas, Saídas e visão do mês

Decisões gerais estão em `docs/arquitetura.md` (normativo). Aqui ficam só as decisões desta feature ou
o que a arquitetura deixou em aberto. Nada aqui muda regra de negócio da spec.

## R1. Parser de valor, `Money` e vetores compartilhados

- **Decision**: `FinMy.Domain/Money/`: `Money` (record struct sobre `long` centavos),
  `ValueExpressionParser.Parse(string) -> Result<long>` (RT-05; descendente recursivo; `decimal` só
  internamente; arredonda meio-para-cima uma única vez no fim; erros `amount.invalid_expression`,
  `amount.division_by_zero`, `amount.must_be_positive`, `amount.too_large`), `MoneySplitter.Split(long, int)`
  (RT-02, sobra na primeira parte) e `TextNormalizer.Normalize(string)` (trim, minúsculas invariantes,
  remove diacríticos por `NormalizationForm.FormD`; NÃO colapsa espaços internos).
  O front tem portas TypeScript idênticas em `frontend/src/app/core/money/`.
- **Vetores**: `shared/test-vectors/value-expression.json`, `money-split.json`,
  `text-normalize.json`. Cada lado tem um teste que lê o arquivo e roda todos os casos.
  Casos mínimos de `value-expression`: `44,99`→4499; `29+73,2`→10220; `239,8/2`→11990;
  `(37,02+37,87)/3`→2496; `1.234,56`→123456; `12.5`→1250; `2x3`→600; `9÷2`→450; `0,005`→1;
  `1/3`→33; `10-10`→erro must_be_positive; `-5`→erro must_be_positive; `1/0`→division_by_zero;
  `1.23`→123; `1.2345`→invalid; `1,2,3`→invalid; `(1+2`→invalid; `""`→invalid;
  `9999999,99`→999999999; `10000000`→too_large; espaços são ignorados (`" 1 + 2 "`→300).
  Menos unário só é aceito no início da expressão ou logo após `(`.
- **Rationale**: RT-05 exige o mesmo resultado na pré-visualização e no servidor.

## R2. `Idempotency-Key`

- **Decision**: filtro de endpoint `IdempotencyFilter` (Api) aplicado com `.WithIdempotency()` a todo
  `POST` de criação desta feature (`/wallets`, `/entries`, `/transfers`, `/entries/{id}/duplicate`).
  Cabeçalho opcional; quando presente deve ser UUID (senão 422 `idempotency.invalid_key`).
  Chave salva em `idempotency_keys` (PK `tenant_id, key`) com hash SHA-256 de
  `método + rota + corpo`. Mesma chave + mesmo hash em 24 h → devolve status e corpo gravados.
  Mesma chave + hash diferente → 422 `idempotency.key_reused`. Só respostas 2xx são gravadas.
  Corrida entre duas requisições com a mesma chave: a segunda recebe 409 `idempotency.in_progress`
  (insere a linha com `status_code = 0` antes de executar; atualiza depois).
  Job diário expurga linhas com `created_at` > 24 h.
- **Rationale**: toque duplo e rede instável no celular (arquitetura §6).

## R3. Exclusão em lote e "Desfazer" (RT-09)

- **Decision**: `DELETE` de lançamentos responde `200 { "deletionBatchId": "<uuid>" }`. O
  `SoftDeleteInterceptor` já grava `deletion_batch_id` a partir de `ICurrentContext.DeletionBatchId`;
  o handler abre o lote com `ctx.BeginDeletionBatch()` (gera UUIDv7) antes de remover.
  `POST /deletions/{batchId}/restore` restaura **todas** as entidades `ISoftDeletable` do Espaço
  com esse lote (varre os tipos do modelo EF; nenhuma lista manual), se `deleted_at` ≥ agora − 10 min;
  senão 404 `deletion.not_found`. Restaurar limpa `deleted_at`, `deleted_by`, `deletion_batch_id`
  e atualiza auditoria. Restaurar duas vezes → 404.
  Job diário remove definitivamente linhas com `deleted_at` > 30 dias.
- **Conflito de unicidade ao restaurar** (ex.: carteira recriada com o mesmo nome): 409
  `deletion.restore_conflict` (só acontece com carteiras; lançamentos não têm índice único).

## R4. Resumo do mês (FR-015) — interpretação do exemplo

- O exemplo da US3 mostra "Saldo inicial 500,00" em março, mês da data do saldo inicial da carteira.
  **Decision**: a API devolve `openingBalanceCents` = Saldo final(M−1) + saldos iniciais das carteiras
  selecionadas com data dentro de M, e devolve também `walletsInitialBalanceCents` (a parte que veio de
  carteiras com saldo inicial em M). O front mostra a linha "Saldo inicial" e, quando
  `walletsInitialBalanceCents ≠ 0`, uma linha secundária "inclui saldo inicial de carteiras R$ X".
  Assim o exemplo bate e a linha própria da FR-015 existe.
- Fórmula final (para Previsto e Realizado; Previsto usa todos, Realizado só `settled`):
  `final(M) = Σ saldo inicial (data ≤ fim de M) + Σ_{m ≤ M} (entradas(m) − saídas(m)) + Σ_{m ≤ M} (transf. recebidas(m) − enviadas(m))`;
  `opening(M) = final(M) − resultado(M) − transfLíquidas(M)`.
  Transferências: mês = mês de `transfer_date`; só entram quando há filtro de carteira que inclui
  exatamente um dos lados (com todas selecionadas se anulam).
- **Implementação**: `MonthSummaryCalculator` (Domain, puro) recebe agregados já somados pelo banco
  (`GROUP BY reference_month, kind, status` com `reference_month ≤ M`; transferências agrupadas por
  mês e carteira; carteiras com saldo inicial). Uma consulta por agregado, nada desnormalizado.
- O filtro de Situação (FR-018) afeta **só as listas**; o resumo sempre mostra Previsto e Realizado.
- Ponto de extensão 003: o calculador recebe também `invoiceForecasts` e `invoicePayments`
  (listas vazias na 002).

## R5. Saldo atual da carteira (FR-003)

- **Decision**: `GET /wallets` devolve `currentBalanceCents` calculado por uma consulta agregada
  (entradas recebidas − saídas pagas + transferências recebidas − enviadas, sem filtro de data).
  Pagamentos de fatura entram na 003.
- Teste de consistência SC-003 (`WalletConsistencyTests`): gera dados aleatórios com semente fixa
  (200 lançamentos, 3 carteiras, transferências, datas espalhadas em 3 anos) e verifica
  Σ saldos atuais = Saldo final Realizado do último mês com lançamento efetivado ou data de saldo inicial.

## R6. Carteira principal e arquivamento (FR-002, US1.4)

- Troca de principal (`POST /wallets/{id}/make-primary`) e arquivamento da principal
  (`POST /wallets/{id}/archive` com `{ "newPrimaryWalletId" }`) rodam numa transação com dois
  `SaveChanges`: primeiro desmarca a antiga, depois marca a nova (o índice único parcial não é
  adiável). Arquivar a principal sem `newPrimaryWalletId` → 422 `wallet.new_primary_required`;
  sem outra ativa → 422 `wallet.last_active_cannot_be_archived`.
- Primeira carteira criada (nenhuma outra ativa) → principal. Desarquivar nunca torna principal,
  exceto se não houver nenhuma ativa.
- **Excluir** (`DELETE /wallets/{id}`): só se não existe nenhuma linha (inclusive soft-deleted ainda
  não expurgada) em `entries`/`transfers` referenciando a carteira; senão 409
  `wallet.has_entries_archive_instead`. Excluir a principal segue a mesma regra de nova principal
  (`?newPrimaryWalletId=`). Exclusão de carteira não tem "Desfazer" (RT-09 remete às specs; a spec
  não pede) — o front confirma com diálogo.

## R7. Situação e Mês de referência no servidor

- `status` é opcional na criação; ausente → regra FR-012 com `IClock.Today`. Sempre obrigatório no
  `PUT`. `referenceMonth` opcional na criação (padrão = mês da data); no `PUT`, regra RT-04:
  se o corpo não traz `referenceMonth` ou traz o mesmo valor atual, e o atual é igual ao mês da data
  antiga, ele acompanha a nova data. Regra pura em `Entry.ChangeDate(newDate)`.
- Carteira do lançamento precisa estar ativa **na criação** e ao **trocar** de carteira; editar outros
  campos de um lançamento de carteira arquivada é permitido. Erro: 422 `wallet.archived`.

## R8. Sugestões de descrição (FR-011)

- `GET /entries/suggestions?q=ti&kind=expense` → até 6 itens
  `{ description, uses, walletId }`. Universo: entries do Espaço com `entry_date ≥ hoje − 12 meses`
  (inclui futuros), `description_normalized LIKE %q%` (curingas escapados), agrupado por
  `description_normalized`; `description` exibida = a do uso mais recente; ordenação: `uses` desc,
  depois uso mais recente. `walletId` = carteira do lançamento mais recente (maior `entry_date`,
  depois `created_at`) com a **mesma descrição normalizada e o mesmo `kind`**; `null` se só existe no
  outro tipo ou se a carteira está arquivada. `q` com menos de 1 caractere → lista vazia.
  (Na 003, compras entram no mesmo universo.)

## R9. Pendências (FR-019/FR-020)

- Pendência = entry `status=planned` com `reference_month < mês de hoje`. Sem filtro de carteira
  (sempre o Espaço todo).
- `GET /months/{m}` traz `pending { count, toReceiveCents, toPayCents }` calculado sempre em relação a
  hoje (o front só exibe quando `m` é o mês de hoje).
- `GET /entries/pending` lista as pendências (RT-12).
- `POST /entries/bulk` `{ ids[], action: "settle" | "move_to_current_month" | "delete" }`: tudo ou
  nada; `delete` usa um único lote e devolve `deletionBatchId`. Ids inexistentes → 404 `not_found` e
  nada é alterado. Máximo 200 ids.

## R10. Busca (FR-025)

- `GET /entries/search?q=&kinds=&walletIds=&status=&from=&to=&cursor=&limit=` busca em entries
  **e** transfers (tipo `transfer` filtrável); `q` casa com `description_normalized` (LIKE). Ordem:
  data desc, depois `created_at` desc (busca é a exceção à RT-12: mais recente primeiro). `limit`
  padrão 50, máx. 200; cursor opaco (base64 de `date|createdAt|id`). Resposta inclui `totalCents`
  de **todos** os resultados (não só da página): entradas positivas, saídas negativas,
  transferências 0.

## R11. Concorrência otimista

- Respostas de recurso trazem `version` (o `xmin`, como número). `PUT` exige `version`; divergente →
  409 `concurrency_conflict` ("Este lançamento foi alterado em outro aparelho. Recarregue.").
  Ações de um toque (trocar situação, bulk) não exigem versão (última escrita vence).

## R12. Front: formulário rápido, preferências do aparelho e offline

- Formulário rápido = componente `LancamentoSheet` aberto pelo "+" global da casca: bottom sheet em
  tela cheia no celular, diálogo no computador. Teclado numérico próprio (arquitetura §9) só em
  telas com ponteiro grosso (`matchMedia('(pointer: coarse)')`); no computador, campo de texto.
- `localStorage` (`finmy.prefs.v1`): `lastKind`, `lastWalletByKind`. Rascunho do formulário em
  `finmy.draft.lancamento` (apagado ao salvar).
- `Idempotency-Key`: gerada ao abrir o formulário (`crypto.randomUUID()`), reaproveitada em
  reenvios do mesmo formulário, renovada após salvar com sucesso.
- Offline: `navigator.onLine === false` ou erro HTTP status 0 → snackbar "Sem conexão. O lançamento
  não foi salvo; tente de novo." e o formulário continua aberto e preenchido.
- "Atrasada" e "data anterior ao saldo inicial da carteira" são calculados no front com o `today`
  devolvido por `GET /months/{m}` (não pelo relógio do aparelho).
- Navegação entre meses: setas e gesto de deslizar horizontal (eventos de ponteiro, limiar 60 px);
  filtros ficam na query string (`/mes/2026-03?carteiras=a,b&situacao=planned`) e são mantidos.
- Rota `/` redireciona para `/mes/<mês de hoje>` (a 006 troca pela preferência de tela inicial).

## R13. Cores de carteira

- Paleta fixa de 10 cores (Material) oferecida no formulário; API aceita qualquer `#RRGGBB`
  (`CHECK` + validação). Padrão: próxima cor da paleta ainda não usada.
