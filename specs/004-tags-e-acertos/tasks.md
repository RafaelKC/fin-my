---
description: "Tarefas da feature 004 — Tags e acerto de contas"
---

# Tasks: Tags e acerto de contas

**Input**: `specs/004-tags-e-acertos/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/openapi.yaml](contracts/openapi.yaml), [contracts/ui-rotas.md](contracts/ui-rotas.md),
[quickstart.md](quickstart.md). Normas: `docs/arquitetura.md`, `docs/modelo-de-dados.md`, constituição, research da 002 e 003.

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: 003 concluída na `main`. Dados fictícios.

## Phase 1: Fundação

- [x] T001 Entidade `Tag`, ligações e colunas novas conforme data-model.md (configurações EF e migração `Tags`), `SettlementCalculator` e `SplitPlanner` puros em `backend/src/FinMy.Domain/` com `SettlementCalculatorTests` (exemplo da US2, US2.2, US2.3, acumulado) e `SplitPlannerTests` (US4.1–US4.4)

## Phase 2: US1/US5 — Tags

- [x] T002 [US1] API `/tags*` (listar com `uses90d` e `inUse`, criar com `existingTagId` no 409, editar com troca de `isSettlement` movendo ligações e conflito `tag.settlement_conflict`, arquivar, desarquivar, excluir só sem uso, juntar) — research R2, R7; testes HTTP de US1.2, US1.3, US5.1–US5.4. Depende de T001
- [x] T003 [US1] `tagIds` em entradas, saídas e compras (criar, editar, parcela com `scope`), `settlementTagId` nas respostas, sugestões com `tagIds`, filtros `tagIds` em `GET /months/{m}` (com `purchases[]` e `filterTotals`), na fatura (`filteredTotalCents`) e na busca (compras entram como `card_purchase`), `settlementSubtotals` na fatura — research R1, R6; testes HTTP de US1.4, US1.5, US2.4 e SC-004. Depende de T002
- [x] T004 [P] [US1] Componente `TagPicker` (`frontend/src/app/shared/tag-picker/`) e campo Tags no formulário rápido (Entrada/Saída/Cartão; sugestão aplica tags; edição de parcela pergunta escopo) e filtros por tag na visão do mês, na fatura e na busca (contracts/ui-rotas.md); e2e `tags.spec.ts` e `filtro-tags.spec.ts` (US1.1–US1.5; SC-001). Depende de T003
- [ ] T005 [P] [US5] Tela `/tags` (contracts/ui-rotas.md), item na casca; e2e `gerenciar-tags.spec.ts` (US5.1–US5.4). Depende de T002

## Phase 3: US2/US3 — Acertos

- [ ] T006 [US2] API `/settlements/{month}` e `/settlements/{month}/tags/{tagId}/items` (research R3) com teste HTTP do exemplo da US2 (inclui compras de fatura e crédito) e parcela contando no mês da fatura. Depende de T003
- [ ] T007 [US2] Tela `/acertos/:mes` (contracts/ui-rotas.md) com detalhamento dos números, "Gerar cobrança" pré-preenchendo o formulário rápido (research R4, US3.1–US3.4), bloco "Por pessoa" na fatura (FR-011), casca com "Acertos" na barra inferior; e2e `acertos.spec.ts`. Depende de T004, T006

## Phase 4: US4 — Dividir

- [ ] T008 [US4] API `POST /entries/split` e `POST /card-purchases/split` (research R5; idempotência); testes HTTP de US4.1–US4.5 (inclui compra parcelada dividida). Depende de T003
- [ ] T009 [US4] "Dividir" no formulário rápido (Saída e Cartão) e selo "Dividida em N" com as outras partes (contracts/ui-rotas.md); e2e `dividir.spec.ts`. Depende de T004, T008

## Phase 5: Polish

- [ ] T010 Validação: isolamento (rotas e `tagIds` de outro Espaço), quickstart contra a stack real nos dois viewports (registrar no PR), `docs/modelo-de-dados.md`/`docs/arquitetura.md` §14/README atualizados. Depende de todas

## Dependências (resumo)

T001 → T002 → T003 → {T004, T006, T008}; T002 → T005; T004 + T006 → T007; T004 + T008 → T009.
