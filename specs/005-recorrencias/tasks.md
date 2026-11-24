---
description: "Tarefas da feature 005 — Recorrências"
---

# Tasks: Lançamentos recorrentes

**Input**: `specs/005-recorrencias/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [quickstart.md](quickstart.md).
Normas: `docs/arquitetura.md`, `docs/modelo-de-dados.md`, constituição, research da 002–004.

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: 004 concluída na `main`. Dados fictícios.

- [x] T001 Entidade `Recurrence`, `RecurrenceScheduler` (research R1) e migração `Recurrences` (data-model.md); `RecurrenceSchedulerTests`
- [x] T002 [US1] `RecurrenceGenerator`, job diário `RecurrenceHorizonJob` e API `GET/POST /recurrences`, `GET /recurrences/{id}` (research R2, R6; contracts/api.md); testes HTTP de US1.1–US1.6, US2.1, arquivados e `RecurrenceHorizonJobTests`. Depende de T001
- [ ] T003 [US3] Marcação "alterada" em `PUT /entries/{id}` e `PUT /card-purchases/{id}` (research R3), `PUT /recurrences/{id}` com `fromMonth`, `POST /recurrences/{id}/end`, `DELETE /recurrences/{id}` (research R4); testes de US2.2, US3.1–US3.4. Depende de T002
- [ ] T004 [US4] `POST /recurrences/from-item` (research R5); testes de US4.1 e dos casos não elegíveis. Depende de T002
- [ ] T005 [P] [US1] Telas `/recorrencias`, `/recorrencias/nova`, `/recorrencias/:id` (contracts/api.md "Telas"), ícone de recorrência e "alterada" nas listas, item no menu; e2e `recorrencias.spec.ts` (criar, alterar a partir de, encerrar, excluir). Depende de T002, T003
- [ ] T006 [US4] "Repetir todo mês" no formulário rápido em modo edição; e2e. Depende de T004, T005
- [ ] T007 Validação: isolamento, quickstart contra a stack real, docs (`docs/arquitetura.md` §14, README). Depende de todas

## Dependências (resumo)

T001 → T002 → {T003, T004}; T002 + T003 → T005; T004 + T005 → T006 → T007.
