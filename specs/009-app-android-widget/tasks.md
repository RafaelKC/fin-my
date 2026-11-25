---
description: "Tarefas da feature 009 — App Android (TWA) com widget de cartão"
---

# Tasks: App Android (TWA) com widget de cartão

**Input**: `specs/009-app-android-widget/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [quickstart.md](quickstart.md).
Normas: `docs/arquitetura.md`, constituição, research da 003, 007 e 008.

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: 008 e 007 concluídas na `main`. Dados fictícios.

## Phase 1: Servidor

- [x] T001 [US2] Cliente OAuth `finmy-android` (seed idempotente, research R3), recurso `<APP_URL>/api/v1/widget` nos tokens desse cliente, seletor de esquema e exigência de recurso por grupo de rotas (FR-004), `channel = android_widget`, `GET /api/v1/widget/credit-cards` e `GET /api/v1/widget/credit-cards/{id}/current-invoice` (research R4, contracts/api.md), `GET /.well-known/assetlinks.json` por configuração (research R2); `WidgetApiTests` (inclui SC-002 contra a rota da fatura, isolamento, 401 com JWT do app e com token do Claude, token do widget negado no `/mcp`), `AssetLinksTests`, fluxo OAuth completo do `finmy-android` em `OAuthFlowTests`
- [x] T002 [US2] Front: `/autorizar` e Aplicativos conectados com nome e texto por cliente (FR-006, contracts/api.md "Telas"); testes unitários e e2e (autorizar com `client_id=finmy-android`). Depende de T001

## Phase 2: Android

- [x] T003 [US1] Projeto `android/` (research R1, R6, R7): TWA com `LauncherActivity`, `applicationId br.app.finmy`, `finmy.appUrl`, ícones/cores da identidade atual (`frontend/public/`), splash, atalhos, `network_security_config` de debug com `finmy.devCert` opcional, `keystore.properties` git-ignorado para release; job `android` no CI (lint, testes unitários, `assembleDebug`, APK como artifact); `integrations/android/README.md` (build, instalar APK, certificado da VPN no celular)
- [x] T004 [US2] Widget "Fatura do cartão" (research R3, R5): login AppAuth + PKCE com tokens cifrados, activity de configuração (entrar e escolher cartão), Glance com Atual/Planejado/vencimento/situação/restante, toque abre a fatura na TWA, atualizar manual e WorkManager 30 min, estados offline/"Entre de novo"/"Cartão indisponível", claro/escuro; testes unitários (cliente HTTP com MockWebServer, formatação pt-BR, estados) e Robolectric/Glance. Depende de T001, T003

## Phase 3: Polish

- [ ] T005 Validação: isolamento, quickstart contra a stack real com emulador (registrar no PR), `docs/arquitetura.md` (§ app Android e §14), README. Depende de todas

## Dependências (resumo)

T001 → T002; T003 (paralela a T001/T002); T001 + T003 → T004 → T005.
