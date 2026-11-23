---
description: "Tarefas da feature 003 — Cartões, Compras parceladas e Faturas"
---

# Tasks: Cartões de crédito, Compras parceladas e Faturas

**Input**: `specs/003-cartoes-e-faturas/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/openapi.yaml](contracts/openapi.yaml), [contracts/ui-rotas.md](contracts/ui-rotas.md),
[quickstart.md](quickstart.md). Normas: `docs/arquitetura.md`, `docs/modelo-de-dados.md`, constituição, e as
decisões da 002 (`specs/002-carteiras-e-lancamentos/research.md`).

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: feature 002 concluída na `main`. Dados fictícios.

## Phase 1: Fundação

- [x] T001 [P] Regras puras em `backend/src/FinMy.Domain/Cards/`: `InvoiceCycle`, `InvoiceStatusRule`, `InstallmentGenerator` (research R1) e os vetores `shared/test-vectors/invoice-cycle.json`, `invoice-status.json`, `installments.json` com todos os exemplos da spec (tabela "Regra de fatura", cartões Verde, X e Y, US3.1, US3.2, US4.1) e as fronteiras de research R1; testes que leem os vetores
- [x] T002 [P] Porta TS em `frontend/src/app/core/money/` de `InvoiceCycle` e `InstallmentGenerator` (pré-visualização) com specs lendo os mesmos vetores (se a T001 não estiver na `main`, criar os JSON com os casos da spec e a T001 completa sem divergir)
- [x] T003 Entidades, configurações EF e migração `CreditCards` conforme data-model.md (5 tabelas, FK tripla compra→fatura, `CHECK`s, índices), `InvoiceRepository.GetOrCreate` (research R2); testes de constraints no Postgres (compra não pode apontar para fatura de outro cartão; nome único; crédito parcelado recusado pelo `CHECK`). Depende de T001

## Phase 2: US1/US6 — Cartões

- [ ] T004 [US1] API `/credit-cards*` (criar, listar com `CreditCardOverview` — fatura atual, próxima, em aberto, limite disponível FR-026 —, obter, editar com redistribuição ao mudar dias (research R3), arquivar, desarquivar, excluir só sem compras/pagamentos, `invoice-for`); testes HTTP de US1.1–US1.3, US6.1, US6.3 e `CardScheduleChangeTests`. Depende de T003
- [ ] T005 [P] [US1] Telas `/cartoes`, `/cartoes/novo`, `/cartoes/:id/editar` (contracts/ui-rotas.md) e ajuste da casca (Cartões na barra inferior, Carteiras em "Mais"); unidade + e2e `cartoes.spec.ts` (US1.1–US1.3, US6.1, US6.3). Depende de T004

## Phase 3: US2–US4 — Compras

- [ ] T006 [US2] API `/card-purchases` e `/installment-plans/{id}`: criar à vista, parcelada (dois modos) e em andamento; natureza/estorno (research R6); confirmação de fatura paga (research R5); obter; editar com `scope` (research R7, FR-015); excluir com `scope` (lote; par de saldo levado junto); `reconciled`; `move` (FR-028); sugestões com `kind=card_purchase` e `creditCardId` (research R9); idempotência no POST; testes HTTP de US2.1–US2.4, US3.1, US3.2, US3.4, US3.5, US4.1, US4.2, US7.2, US7.3. Depende de T003, T004
- [ ] T007 [US2] Aba "Cartão" no formulário rápido (`features/lancamento/`): tipo padrão Cartão no primeiro uso, cartão padrão (research R9), parcelas e modos, parcelamento em andamento, pré-visualização da fatura e das parcelas pela porta TS (US2.1, US3.3), aviso de crédito, diálogo de confirmação de fatura paga, sugestões aplicando cartão; e2e `compra.spec.ts` e `parcelas.spec.ts` (US2, US3.1–US3.3, US4; SC-001). Depende de T002, T006

## Phase 4: US5/US7 — Fatura e pagamento

- [ ] T008 [US5] API de fatura: `GET /credit-cards/{id}/invoices/{month}` (compras RT-12, totais, situação, `reconciledCents`, `canCarryOver`, pagamentos, meses vizinhos), pagamentos (`POST`/`DELETE` em `…/payments`, `PUT`/`DELETE /invoice-payments/{id}`, bloqueio `FOR UPDATE`, research R4), `carry-over` (FR-020, inclusive sentido inverso com total negativo); saldo atual da carteira descontando pagamentos e `WalletConsistencyTests` atualizado; `InvoiceExampleTests` com o exemplo numérico da US5 e US5.1–US5.7, US7.1. Depende de T006
- [ ] T009 [US5] Tela `/cartoes/:id/faturas/:mes` (contracts/ui-rotas.md): navegação entre faturas, totais e situação, conferido, lista com caixinha de conferida e ações (editar abre o formulário, mover, excluir com escopo e Desfazer), pagamentos (pagar com valor/data/carteira, editar, excluir), levar restante; e2e `fatura.spec.ts` (US5.2–US5.5, US6.2, US7.1–US7.3). Depende de T005, T008

## Phase 5: Integração com o mês

- [ ] T010 Resumo e visão do mês com faturas (research R8): `MonthSummaryCalculator` recebendo faturas (Previsto `max(Total, Pago, 0)`, Realizado Pago, recorte por carteira FR-023), `GET /months/{m}` com `invoices[]`, `InvoiceWalletSplitTests` (SC-004) e teste do exemplo da US5 na visão de março. Depende de T008
- [ ] T011 Linhas "Fatura <Cartão>" na visão do mês (front): situação, Total/Pago/Restante, caixinha pagar/desfazer pagamentos (FR-019, US5.1, US5.6), toque abre a fatura; edição/exclusão de parcela com os diálogos de escopo no formulário rápido (US3.4, US3.5); e2e `mes-faturas.spec.ts`. Depende de T007, T009, T010

## Phase 6: Polish

- [ ] T012 Validação: isolamento cobre as rotas novas; quickstart.md roteiro manual contra a stack real nos dois viewports (registrar no PR); atualizar `docs/modelo-de-dados.md` (único de `invoices` com `tenant_id`), `docs/arquitetura.md` §14 e README. Depende de todas

## Dependências (resumo)

T001 → T003 → T004 → T006 → T008 → T010; T001 → T002 → T007; T004 → T005 → T009; T006 + T002 → T007;
T007 + T009 + T010 → T011. Trilhas: back (T001, T003, T004, T006, T008, T010) e front (T002, T005, T007, T009, T011).
