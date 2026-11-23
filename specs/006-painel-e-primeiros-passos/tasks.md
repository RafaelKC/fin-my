---
description: "Tarefas da feature 006 — Painel e primeiros passos"
---

# Tasks: Painel anual, resumo de agora e primeiros passos

**Input**: `specs/006-painel-e-primeiros-passos/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[contracts/api.md](contracts/api.md), [quickstart.md](quickstart.md). Normas: `docs/arquitetura.md`, constituição,
research da 002–005.

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: 005 concluída na `main`. Dados fictícios.

- [ ] T001 [US1] `GET /years/{yyyy}/panel` (research R1) com `PanelParityTests` e `PanelPerformanceTests`
- [ ] T002 [P] [US2] `GET /now` (research R2); testes HTTP de US2.1–US2.4 e carteiras arquivadas
- [ ] T003 [P] [US3] Estado do guia: colunas e migração `Onboarding`, `GET /onboarding`, `PUT /onboarding/steps/{step}`, `POST /onboarding/dismiss` (research R3); testes
- [ ] T004 [P] [US4] `GET /exports/entries.csv` (research R4); `CsvExportTests`
- [ ] T005 [US1] Tela `/painel/:ano` com "Agora", tabela, alternância, destaques, gráfico SVG (research R5) e preferência de tela inicial no Perfil (research R6); e2e `painel.spec.ts` (US1.1–US1.6, US2). Depende de T001, T002
- [ ] T006 [US3] Guia `/primeiros-passos/:etapa` reaproveitando os formulários, abertura automática com `autoStart` e cartão "Continuar configuração" no Painel; e2e `primeiros-passos.spec.ts` (US3.1–US3.4). Depende de T003, T005
- [ ] T007 [US4] Exportar CSV no Perfil (mês inicial/final, download); e2e `exportar.spec.ts` lendo o arquivo baixado. Depende de T004
- [ ] T008 Validação: isolamento, quickstart contra a stack real, docs (§14, README). Depende de todas

## Dependências (resumo)

{T001, T002} → T005 → T006; T003 → T006; T004 → T007; todas → T008.
