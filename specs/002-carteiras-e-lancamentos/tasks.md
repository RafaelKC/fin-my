---
description: "Tarefas da feature 002 — Carteiras, Entradas, Saídas e visão do mês"
---

# Tasks: Carteiras, Entradas, Saídas e visão do mês

**Input**: `specs/002-carteiras-e-lancamentos/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/openapi.yaml](contracts/openapi.yaml), [contracts/ui-rotas.md](contracts/ui-rotas.md),
[quickstart.md](quickstart.md). Normas: `docs/arquitetura.md` (inclusive §14, desvios da 001),
`docs/modelo-de-dados.md`, `.specify/memory/constitution.md`, `backend/tests/README.md`.

**Testes**: obrigatórios. Cada tarefa entrega código **e** testes no mesmo PR; CI verde.

**Unidade de entrega**: 1 tarefa = 1 card = 1 branch = 1 PR. Agentes não editam este arquivo.

**Dados fictícios** em testes e exemplos (Lia, Léo, Bruna, Carla, Duda; bancos "Azul"/"Verde").

## Format: `[ID] [P?] [Story] Description` — [P] = pode rodar em paralelo

---

## Phase 1: Fundação da 002

- [x] T001 [P] Domínio de dinheiro em `backend/src/FinMy.Domain/Money/`: `Money`, `YearMonth`, `ValueExpressionParser` (RT-05, menos unário só no início ou após `(`, erros `amount.*` de research R1), `MoneySplitter` (RT-02) e `TextNormalizer`; criar `shared/test-vectors/value-expression.json` (todos os casos de research R1 e mais os que achar), `money-split.json` (100,00/3; 239,80/2; 0,01/3; 1,00/1) e `text-normalize.json` (acentos, maiúsculas, espaços nas pontas, ç, espaços internos preservados), conforme o formato de `shared/test-vectors/README.md`; testes em `backend/tests/FinMy.Domain.Tests/Money/` que leem os vetores (caminho relativo à raiz do repo) e testes de `YearMonth`
- [x] T002 [P] Porta TS em `frontend/src/app/core/money/`: `parseValueExpression`, `splitMoney`, `normalizeText` (mesmas regras e códigos de erro da T001), pipe `brl` (`R$ 1.234,56`, negativo `-R$ 1.234,56`), utilitários de mês (`AAAA-MM` ↔ "Março de 2026" / "MAR/26", mês anterior/seguinte, mês de uma data) e `today()` em America/Sao_Paulo; specs que leem os mesmos `shared/test-vectors/*.json` (configurar o Vitest para importar JSON fora de `src`). Depende dos vetores da T001: se a T001 não estiver na `main`, criar os três JSON com os casos de research R1 e a T001 completa depois (sem divergir)
- [x] T003 Entidades `Wallet`, `Entry`, `Transfer` em `backend/src/FinMy.Domain/{Wallets,Entries,Transfers}/` com as regras de data-model.md (FR-012 situação padrão com `today`, RT-04 em `Entry.ChangeDate`, `MoveToMonth`, validações de tamanho e intervalo de datas RT-03), configurações EF em `Persistence/Configurations/` com todas as colunas, `CHECK`s, índices e FKs compostas (`HasTenantForeignKey`) e a migração `Wallets`; testes de domínio `EntryRulesTests` (US2.1, US2.3, US6.1, US6.2, FR-012 com data = hoje) e teste de migração/constraints no Postgres (índice único de principal, nome normalizado único, check de transferência). Depende de T001
- [x] T004 Idempotência (research R2): tabela `idempotency_keys` (configuração + migração própria `IdempotencyKeys`; incluir na lista global do teste de arquitetura), `IdempotencyFilter` e extensão `.WithIdempotency()` em `backend/src/FinMy.Api/Idempotency/`, job diário de expurgo (> 24 h) em `Infrastructure/Jobs/`; testes HTTP com um endpoint de teste ou com `POST /wallets` se já existir (mesma chave → mesma resposta sem duplicar; corpo diferente → 422 `idempotency.key_reused`; chave inválida → 422; chave de outro Espaço não colide)
- [x] T005 Restaurar exclusão (research R3): `ICurrentContext.BeginDeletionBatch()`, `POST /api/v1/deletions/{batchId}/restore` em `Endpoints/Deletions/` restaurando todas as entidades `ISoftDeletable` do Espaço com o lote (varrendo o modelo EF, sem lista manual), janela de 10 min via `IClock`, 404 `deletion.not_found`, 409 `deletion.restore_conflict` em violação de índice único; job diário que expurga linhas com `deleted_at` > 30 dias em todas as tabelas `ISoftDeletable`; testes com as sondas `ProbeItem` (restaurar, expirado, outro Espaço, duas vezes) — incluir a rota na suíte de isolamento

**Checkpoint**: vetores verdes nos dois lados; tabelas criadas; idempotência e desfazer prontos.

---

## Phase 2: US1 — Carteiras (P1)

- [x] T006 [US1] API de carteiras conforme contracts/openapi.yaml (`/wallets*`) em `Application/Wallets/` e `Endpoints/Wallets/`: criar (primeira ativa vira principal; `initialBalance` aceita zero/negativo — usar o parser permitindo sinal só aqui), listar com `currentBalanceCents` e `hasEntries` (research R5) e filtro `archived`, obter, editar com `version`, `make-primary`, `archive` (com `newPrimaryWalletId`), `unarchive`, `delete` (research R6); `.WithIdempotency()` no POST; testes HTTP de US1.1–US1.4 e dos códigos de erro
- [x] T007 [P] [US1] Telas `/carteiras`, `/carteiras/nova` e `/carteiras/:id` em `frontend/src/app/features/carteiras/` conforme contracts/ui-rotas.md (lista com saldo e cor, principal marcada, filtro Arquivadas; formulário com paleta de 10 cores e saldo inicial com expressão; Tornar principal; Arquivar pedindo nova principal quando for a principal; Excluir só sem lançamentos com diálogo de confirmação), serviço `WalletsService`, item "Carteiras" na casca (barra inferior e menu lateral conforme ui-rotas "Casca"); testes de unidade e e2e `carteiras.spec.ts` (US1.1, US1.2, US1.4) contra a stack real. Pode começar com API simulada se a T006 não estiver pronta, mas o e2e real é obrigatório antes do PR

---

## Phase 3: US2 — Lançar Entrada/Saída (P1)

- [x] T008 [US2] API de entradas/saídas: `POST /entries` (parser, FR-012 com `IClock.Today`, RT-04, carteira ativa, idempotência), `GET/PUT /entries/{id}` (RT-04 research R7, `version`), `PATCH /entries/{id}/status`, `GET /entries/suggestions` (research R8) em `Application/Entries/` e `Endpoints/Entries/`; testes HTTP de US2.1–US2.6 (pelo lado do servidor), US6.1, US6.2 e das sugestões (ordem por usos, carteira do mesmo tipo, 12 meses, máx. 6, acentos)
- [x] T009 [P] [US2] Componente de valor em `frontend/src/app/shared/amount-input/`: teclado numérico próprio (`0-9 , + − × ÷ ( ) ⌫`, alvos ≥ 44 px) ativo em `pointer: coarse`, campo de texto no computador, pré-visualização "= R$ 102,20" ou mensagem de erro usando a porta TS do parser (T002), `ControlValueAccessor` com o texto digitado; testes de unidade
- [x] T010 [US2] Formulário rápido em `frontend/src/app/features/lancamento/` conforme contracts/ui-rotas.md "Formulário rápido": "+" global na casca (bottom sheet tela cheia no celular, diálogo no computador), tipos Saída/Entrada (Transferência entra na T016), padrões FR-010 com `DevicePrefsService` (`frontend/src/app/core/prefs/`, research R12), sugestões FR-011 (aplicam carteira só se ainda não alterada à mão; nunca o valor), situação FR-012 (para de seguir a data se alterada à mão), "Mais opções" (Mês de referência, Observação), criar carteira inline quando não há nenhuma, aviso de data anterior ao saldo inicial, "Salvar" / "Salvar e lançar outro", `Idempotency-Key` por formulário, mensagem de sem conexão (US2.7), rascunho em `localStorage`; e2e `lancamento.spec.ts` (US2.1–US2.7; SC-001: ≤ 4 toques com sugestão) nos dois viewports. Depende de T008, T009

---

## Phase 4: US3 — Visão do mês (P1)

- [x] T011 [US3] `MonthSummaryCalculator` em `backend/src/FinMy.Domain/Months/` (research R4, com pontos de extensão vazios para faturas) e `GET /months/{month}` em `Application/Months/` + `Endpoints/Months/` (consultas agregadas por mês/kind/status, transferências por mês e carteira, filtro `walletIds`, listas na ordem RT-12, `today`, `pending`); testes: `MonthSummaryCalculatorTests` com o exemplo da US3 (todos os números, abril, US3.2), filtro por carteira com transferências (US5.1, US5.2), carteira com saldo inicial no meio do período; teste HTTP do exemplo; `WalletConsistencyTests` (SC-003, research R5). Depende de T003 (pode criar transferências direto pelo `DbContext` nos testes se a T015 não estiver pronta)
- [x] T012 [US3] Visão do mês em `frontend/src/app/features/mes/` substituindo o placeholder, conforme contracts/ui-rotas.md "Visão do mês": rota `/mes/:mes` e redirecionamento de `/`, resumo Previsto|Realizado com linha de saldo inicial de carteiras, listas de Entradas/Saídas/Transferências, caixinha de situação de um toque (PATCH, recarrega o resumo), selo "Atrasada", vermelho em negativos, navegação por setas e deslizar mantendo filtros na query string, filtros de carteira e situação, estado vazio, tabela ordenável no computador (FR-026), tocar no item abre o formulário rápido em modo edição (o modo edição completo é da T018; aqui basta abrir com os dados e salvar via PUT); e2e `mes.spec.ts` reproduzindo o exemplo da US3 e US3.2–US3.5 nos dois viewports. Depende de T010, T011

---

## Phase 5: US4 — Pendências (P2)

- [x] T013 [US4] `GET /entries/pending` e `POST /entries/bulk` (research R9; `delete` com um único lote) em `Application/Entries/`; testes HTTP de US4.1–US4.4 (inclui desfazer pelo `/deletions/{batchId}/restore`) e tudo-ou-nada com id inexistente. Depende de T005, T008
- [x] T014 [P] [US4] Aviso e lista de pendências na visão do mês (`frontend/src/app/features/mes/pendencias/`): aviso só no mês de hoje com contagem e totais, lista com seleção múltipla e as três ações, Desfazer de 8 s na exclusão; e2e `pendencias.spec.ts` (US4.1–US4.4). Depende de T012, T013

---

## Phase 6: US5 — Transferências (P2)

- [x] T015 [US5] API `/transfers` (criar com idempotência, obter, editar com `version`, excluir com lote) em `Application/Transfers/` e `Endpoints/Transfers/`; testes HTTP de US5.1 (saldos das carteiras via `GET /wallets`), US5.3 e da exclusão + restaurar. Depende de T003, T005
- [x] T016 [P] [US5] Transferência no formulário rápido (tipo "Transferência": De/Para, descrição padrão, sem situação nem sugestões) e na visão do mês (lista recolhida; com filtro de uma carteira "Transferência recebida/enviada" e linhas de transferências no resumo); e2e `transferencias.spec.ts` (US5.1–US5.3). Depende de T012, T015

---

## Phase 7: US6 — Editar, excluir, duplicar, buscar (P2)

- [x] T017 [US6] `DELETE /entries/{id}` (lote), `POST /entries/{id}/duplicate` (idempotência; mês e situação recalculados) e `GET /entries/search` (research R10: entries + transfers, filtros, cursor, `totalCents` e `totalCount` de todos os resultados, LIKE com curingas escapados) ; testes HTTP de US6.3, US6.4, US6.5 (acentos e maiúsculas, vários meses, paginação estável, total). Depende de T005, T008, T015
- [x] T018 [US6] Front: modo edição completo do formulário rápido (todos os campos, `version`, 409 → mensagem "alterado em outro aparelho" e recarregar), Excluir com snackbar "Excluído — Desfazer" 8 s (RT-09) para entradas, saídas e transferências, Duplicar pedindo só a nova data, e a tela `/busca` (`frontend/src/app/features/busca/`) com filtros, rolagem infinita pelo cursor e total; item "Busca" na casca; e2e `editar-excluir-buscar.spec.ts` (US6.1–US6.5). Depende de T012, T017

---

## Phase 8: Polish

- [x] T019 Validação final: conferir que a suíte de isolamento cobre todas as rotas novas, rodar o quickstart.md §3 inteiro contra a stack real nos dois viewports (registrar no PR), atualizar `docs/modelo-de-dados.md` (`transfers.description_normalized` e `notes`; `idempotency_keys` na lista global), `docs/arquitetura.md` §14 com os desvios da 002 (se houver) e o README (rotas novas). Depende de todas

## Dependências (resumo)

T001 → T003 → {T006, T008, T011, T015}; T001 → T002 → T009 → T010; T004 → T006/T008/T015/T017 (por causa do
`.WithIdempotency()`); T005 → {T013, T015, T017};
T006 → T007; T008 + T009 → T010; T010 + T011 → T012; T012 → {T014, T016, T018}.
Trilhas paralelas sugeridas: back (T001, T003, T004, T005, T006, T008, T011, T013, T015, T017) e
front (T002, T009, T007, T010, T012, T014, T016, T018).
