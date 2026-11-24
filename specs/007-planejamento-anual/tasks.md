---
description: "Tarefas da feature 007 — Planejamento do ano"
---

# Tasks: Planejamento do ano

**Input**: `specs/007-planejamento-anual/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[contracts/api.md](contracts/api.md), [quickstart.md](quickstart.md). Normas: `docs/arquitetura.md`, constituição,
research da 002–006.

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: 006 concluída na `main`. Dados fictícios.

- [x] T001 [US2] Estimativa de fatura (research R1): coluna e migração `InvoiceEstimates`, `InvoiceForecast` + vetor `invoice-forecast.json` (e porta TS com spec), uso no `MonthSummaryCalculator`/painel/visão do mês com recorte por carteira, `PUT …/estimate`, `estimateCents`/`estimateActive` nas respostas; `InvoiceEstimateTests` (US2.1–US2.3, US2.6, FR-011, FR-012)
- [x] T002 [US1] `GET /years/{yyyy}/planning` (research R2); `PlanningGridTests` (US1.1, FR-002, SC-002). Depende de T001
- [x] T003 [US1] `PUT /years/{yyyy}/planning/cells` e `POST /years/{yyyy}/planning/fill` (research R3); `PlanningCellTests` (US1.2–US1.6, US1.8, US2.4, US2.5). Depende de T002
- [x] T004 [US3] `GET …/copy-candidates` e `POST …/copy` (research R4); `PlanningCopyTests` (US3.1–US3.4). Depende de T002
- [ ] T005 [US1] Tela `/planejamento/:ano` (research R5): grade com edição inline, totais imediatos, Nova linha, Preencher meses, Transformar em recorrência, lista de lançamentos de célula não editável, versão celular; e2e `planejamento.spec.ts` (US1). Depende de T003
- [ ] T006 [US2] Estimativa na tela da fatura e na linha de fatura do mês; diálogo "Copiar do ano anterior" na grade; e2e (US2.1, US2.3, US3). Depende de T004, T005
- [ ] T007 Validação: isolamento, quickstart contra a stack real, docs (`docs/modelo-de-dados.md` `estimate_cents`, §14, README). Depende de todas

## Dependências (resumo)

T001 → T002 → {T003, T004}; T003 → T005; T004 + T005 → T006 → T007.
