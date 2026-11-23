# Research — 007 Planejamento do ano

Nada aqui cria tipo novo de lançamento: a grade cria/edita/exclui Entradas e Saídas Previstas pelos
handlers da 002, e a estimativa é uma coluna da fatura.

## R1. Estimativa de fatura (FR-009..FR-012)

- Coluna `invoices.estimate_cents bigint null CHECK (estimate_cents > 0 AND estimate_cents <= 999999999)`
  (migração `InvoiceEstimates`). Definir estimativa cria a linha da fatura se faltar (003 R2).
- Regra pura `InvoiceForecast.Planned(total, paid, estimate?, today, closingDate)`: hoje < fechamento e
  estimativa → `max(total, paid, estimate, 0)`; senão `max(total, paid, 0)` (003 FR-022). Vetor
  `shared/test-vectors/invoice-forecast.json` com o exemplo da US2 (as três linhas) e fronteira
  hoje = fechamento.
- Recorte por carteira (FR-011): a diferença `Planned(com estimativa) − Planned(sem)` vai para a carteira
  de pagamento do cartão (soma com a regra da 003 R8). O `MonthSummaryCalculator`, o painel (006) e
  `GET /months/{m}` usam `InvoiceForecast`; a linha da fatura na visão do mês ganha `estimateCents` e
  `estimateActive` ("Previsto R$ 800,00 — lançado R$ 205,53"; depois do fechamento "Estimado R$ 800,00").
- Estimativa não entra em limite disponível, acertos, Total/Restante/situação nem saldo de carteira
  (testes de não regressão).
- `PUT /credit-cards/{id}/invoices/{month}/estimate { amount: string | null }` (null ou vazio remove).
  Tela da fatura (003) ganha o campo "Estimativa".

## R2. Grade (FR-001..FR-008)

- `GET /years/{yyyy}/planning` → `incomeRows`, `expenseRows`, `invoiceRows`, `totals` (os 12 meses da
  leitura Previsto do painel, 006 R1 — mesma fonte, SC-002).
- Linha de entrada/saída: chave `(kind, description_normalized)` dos lançamentos com Mês de referência no
  ano; `description` = a do mais recente; ordem alfabética por `description_normalized`. Metadados da
  linha: `latestWalletId` (se ativa), `latestDay`, `latestTagIds`. Célula: `plannedCents` (soma de todos),
  `itemCount`, `allSettled`, `editable` (0 itens, ou 1 item `planned`), `entryId` (quando 1 item).
- Linha de fatura: um por cartão ativo + arquivados com fatura no ano; célula: `plannedCents` (com
  estimativa), `totalCents`, `estimateCents`, `editable: true` (edita só a estimativa).
- Compras e transferências não viram linhas.

## R3. Editar célula e preencher meses

- `PUT /years/{yyyy}/planning/cells` com `{ kind: income|expense|invoice, description? (entrada/saída),
  creditCardId? (fatura), month, value: string, walletId?, day? }` (`walletId`/`day` só vêm de "Nova
  linha"):
  - fatura → define/remove estimativa;
  - entrada/saída com 0 itens e valor → cria Prevista (data FR-004, carteira FR-005, tags da linha,
    Mês de referência = mês da coluna); com 1 Prevista → altera o valor (via `PUT /entries/{id}`, logo
    ocorrência de recorrência vira "alterada"); valor vazio → exclui (lote, devolve `deletionBatchId`);
  - qualquer outro caso → 409 `planning.cell_not_editable` com `items` (lista para edição individual).
  - Resposta: a linha atualizada + `totals` dos 12 meses (atualização imediata, FR-008).
  - Valor aceita `=` inicial (já suportado pelo parser da 002).
- `POST /years/{yyyy}/planning/fill { kind, description?, creditCardId?, amount, months[], walletId?, day? }`
  → `{ created, skipped }`: só meses vazios (sem item na linha / sem estimativa).
- "Transformar em recorrência": só no front, abre o formulário da 005 com descrição, tipo, carteira, dia
  e o valor mais frequente das células com valor (empate: o maior).

## R4. Copiar do ano anterior (FR-013)

- `GET /years/{yyyy}/planning/copy-candidates` → linhas do ano anterior **sem** ocorrências de
  recorrência, com chave `(kind, description_normalized, settlement_tag_id)` (partes de divisão separadas
  por tag de acerto), valores por mês e `hasEstimates` para cartões.
- `POST /years/{yyyy}/planning/copy { rowKeys[], copyEstimates }` → `{ created, skipped }`: para cada
  lançamento de origem, cria Prevista no mesmo mês do ano novo, mesmo dia (RT-03), mesma carteira (se
  arquivada, a principal), mesmas tags, Mês de referência = o da origem + 1 ano; pula se a linha de
  destino (mesma chave) já tem item naquele mês. Estimativas: cada fatura do ano sem estimativa recebe
  a do mesmo mês do ano anterior. Sem vínculo com a origem.

## R5. Front

- Rota `/planejamento/:ano`. Computador: tabela com 1ª coluna fixa, células editáveis inline (Enter/Tab
  salvam e vão para a próxima, Esc cancela), indicação de "todos efetivados", menus de linha (Preencher
  meses, Transformar em recorrência), "Nova linha" (tipo, descrição, carteira, dia; vive só no estado
  da tela), "Copiar do ano anterior" (diálogo com checkboxes). Celular: rolagem horizontal, 1ª coluna
  fixa, edição de célula por diálogo; Preencher/Copiar escondidos (< 1024 px).
- Célula não editável abre painel com os lançamentos (editar pelo formulário rápido).
