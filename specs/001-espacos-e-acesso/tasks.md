---
description: "Tarefas da feature 001 — Contas, Espaços e fundação técnica"
---

# Tasks: Contas de usuário e Espaços isolados (+ fundação técnica)

**Input**: `specs/001-espacos-e-acesso/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/openapi.yaml](contracts/openapi.yaml), [contracts/ui-rotas.md](contracts/ui-rotas.md),
[quickstart.md](quickstart.md). Normas: `docs/arquitetura.md`, `docs/modelo-de-dados.md`, `.specify/memory/constitution.md`.

**Testes**: obrigatórios (constituição T2 e Princípio III; suítes listadas em quickstart.md §2). Cada
tarefa entrega o código **e** seus testes no mesmo PR; testes escritos antes da implementação.

**Unidade de entrega**: 1 tarefa = 1 card = 1 branch = 1 PR. Um PR só é aberto com build e testes
verdes localmente e no CI (a partir da T005).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: US1…US5 da spec

---

## Phase 1: Setup (estrutura do monorepo)

**Purpose**: esqueleto que compila, sobe em containers e roda no CI, ainda sem regra de negócio.

- [x] T001 Criar a solução .NET em `backend/FinMy.slnx` com `src/FinMy.Domain`, `src/FinMy.Application`, `src/FinMy.Infrastructure`, `src/FinMy.Api` e `tests/FinMy.Domain.Tests`, `tests/FinMy.Application.Tests`, `tests/FinMy.Api.Tests`, `tests/FinMy.Architecture.Tests`; referências entre camadas conforme `docs/arquitetura.md` §1; `backend/Directory.Build.props` (net10.0, nullable, warnings como erro, `InvariantGlobalization=false`); `backend/Directory.Packages.props` (versões centralizadas: EF Core 10, Npgsql, EFCore.NamingConventions, FluentValidation, Serilog, MailKit, Hangfire, xUnit, Shouldly, Testcontainers.PostgreSql, NetArchTest.Rules); `.editorconfig` na raiz; um teste trivial por projeto de teste passando com `dotnet test`
- [x] T002 [P] Criar o app Angular em `frontend/` (última versão estável, standalone, SCSS, roteamento) com Angular Material 3, `LOCALE_ID` pt-BR e `registerLocaleData`, ESLint + Prettier, Playwright em `frontend/e2e/` com projetos `mobile` (360×800) e `desktop` (1280×800) e um teste de fumaça; `npm test`, `npm run lint` e `npm run build` passando
- [x] T003 [P] Criar `backend/Dockerfile` (multi-stage sdk→aspnet, usuário não-root, `HEALTHCHECK` em `/health`), `frontend/Dockerfile` (multi-stage node→nginx) e `frontend/nginx.conf` (SPA fallback para `index.html`, proxy de `/api`, `/mcp` e `/health` para `api:8080`, cabeçalhos de segurança básicos) conforme `docs/arquitetura.md` §13
- [ ] T004 Criar `docker-compose.yml` (serviços `db` PostgreSQL 17 com volume e healthcheck; `api` dependente de `db` saudável e executando `migrate` antes de subir; `web` única porta exposta 8080), `docker-compose.dev.yml` (Mailpit em 8025/1025, porta do Postgres exposta para debug) e `.env.example` (connection string, chave JWT, SMTP, `APP_URL`); `.env` no `.gitignore`
- [ ] T005 Criar `.github/workflows/ci.yml` com jobs `backend` (`dotnet format --verify-no-changes`, build, `dotnet test` com Testcontainers), `frontend` (lint, test, build), `docker` (build das duas imagens) e `e2e` (sobe o compose e roda Playwright nos dois projetos); disparo em PR e push na `main`
- [x] T006 [P] Criar `shared/test-vectors/README.md` explicando o formato JSON dos vetores compartilhados (usados a partir da 002, `docs/arquitetura.md` §5)

**Checkpoint**: `docker compose up` sobe `db`, `api` (com `/health` respondendo após T013) e `web`; CI verde.

---

## Phase 2: Foundational (fundação técnica — bloqueia todas as user stories)

**Purpose**: contexto ambiente, Espaço/auditoria/soft delete automáticos, persistência, API base,
autenticação e casca do front. Nenhuma user story começa antes do checkpoint desta fase.

- [x] T007 Criar em `backend/src/FinMy.Domain/Common/` as interfaces `IEntity`, `ITenantEntity`, `IAudited` (incl. `CreatedVia`/`UpdatedVia`), `ISoftDeletable`, `IArchivable`, a classe base `TenantEntity` (setters de `TenantId` e auditoria não públicos), `SystemUser` (id fixo, research R9) e o enum `Channel` (`app`, `claude`, `system`), conforme `docs/arquitetura.md` §3.2 e §4; testes em `backend/tests/FinMy.Domain.Tests/Common/`
- [x] T008 Criar `ICurrentContext` e `IClock` em `backend/src/FinMy.Application/Abstractions/` e as implementações `CurrentContext` (com `BeginSystemScope`, `TenantIdOrEmpty`, `DeletionBatchId`) em `backend/src/FinMy.Infrastructure/Context/` e `SystemClock` (America/Sao_Paulo, RT-03) em `backend/src/FinMy.Infrastructure/Time/`; testes de `Today` na virada de dia em BRT
- [x] T009 Criar `FinMyDbContext` e a extensão `ApplyFinMyConventions` em `backend/src/FinMy.Infrastructure/Persistence/` (snake_case, filtros nomeados `Tenant` fail-closed e `SoftDelete`, `xmin` como token de concorrência, helper de FK composta `(tenant_id, x_id)` com `UNIQUE (tenant_id, id)`), conforme `docs/arquitetura.md` §3.3, §3.5 e research R1/R2; criar `TestFinMyDbContext` com as sondas `ProbeParent`/`ProbeItem` e o fixture Testcontainers em `backend/tests/FinMy.Application.Tests/Infrastructure/`; testes `CompositeForeignKeyTests` (banco recusa referência entre Espaços) e de leitura sem contexto (nenhuma linha)
- [x] T010 Implementar `TenantInterceptor`, `SoftDeleteInterceptor` e `AuditInterceptor` em `backend/src/FinMy.Infrastructure/Persistence/Interceptors/` e registrá-los na ordem da `docs/arquitetura.md` §3.4; testes `TenantInterceptorTests`, `AuditInterceptorTests` e `SoftDeleteTests` com as sondas (cobrindo todos os itens da tabela de quickstart.md §2, inclusive `IgnoreQueryFilters(["SoftDelete"])` mantendo o filtro de Espaço)
- [x] T011 [P] Criar os testes de arquitetura em `backend/tests/FinMy.Architecture.Tests/` (dependências entre camadas; toda entidade mapeada é `ITenantEntity` ou está na lista global de `docs/arquitetura.md` §3.2; nenhum `IgnoreQueryFilters()` sem nome nem com `"Tenant"` fora de `Infrastructure/Jobs`; nenhum uso de `DateTime.Now`/`DateTime.UtcNow`/`DateTimeOffset.Now` fora de `SystemClock`; `Application` não referencia `Microsoft.AspNetCore.Http`)
- [ ] T012 Criar as entidades `User`, `Tenant`, `TenantMember`, `RefreshToken`, `PasswordResetToken`, `LoginThrottle` em `backend/src/FinMy.Domain/` e suas configurações EF em `backend/src/FinMy.Infrastructure/Persistence/Configurations/`, com todas as colunas, tipos, índices e restrições de data-model.md (`User.email_normalized` único, `TenantMember.role`, `TenantMember` com `UNIQUE (user_id)` e `UNIQUE (tenant_id)`, `RefreshToken.token_hash` único, `RefreshToken.revoked_reason`, `PasswordResetToken.token_hash` único, `LoginThrottle.email_normalized` como PK) e a migração inicial `Initial` em `backend/src/FinMy.Infrastructure/Persistence/Migrations/`; teste que aplica as migrações num Postgres vazio
- [ ] T013 Criar a base da API em `backend/src/FinMy.Api/Program.cs`: composição de DI por camada, Serilog JSON (sem e-mail nem dados financeiros), ProblemDetails com `code` e `detail` pt-BR e mapeamento de erros de domínio/validação em `backend/src/FinMy.Api/Errors/` (contracts/openapi.yaml `Problem`, `ValidationProblem`), `GET /health` (servidor + banco), rate limiter nomeado `auth` (research R5) e o comando `migrate` (`dotnet FinMy.Api.dll migrate` aplica migrações e sai); testes em `backend/tests/FinMy.Api.Tests/` com `WebApplicationFactory` + Testcontainers (fixture reutilizável e helpers para criar conta e obter token, usados pelas fases seguintes)
- [ ] T014 Implementar autenticação base em `backend/src/FinMy.Infrastructure/Auth/` e `backend/src/FinMy.Infrastructure/Context/`: `JwtTokenService` (access token 15 min com `sub`, `tid`, `sid`, `name`, `channel`), `PasswordHasherAdapter` (`PasswordHasher<User>`), `RefreshTokenStore` (token opaco 256 bits, hash SHA-256, cookie `finmy_rt` conforme contracts/openapi.yaml `RefreshCookie`), configuração `JwtBearer` e `CurrentContextMiddleware` que preenche `ICurrentContext` a partir das claims **já validadas**; erro 401 com `code=token_expired` quando o token expira; testes
- [ ] T015 Implementar o `SessionValidationMiddleware` (sessão `sid` ativa, cache em memória invalidado na revogação, 401 `session_revoked`) em `backend/src/FinMy.Infrastructure/Context/`, conforme research R3; testes de revogação valendo na requisição seguinte
- [x] T016 [P] Criar `IEmailSender` em `backend/src/FinMy.Application/Abstractions/`, `SmtpEmailSender` (MailKit) em `backend/src/FinMy.Infrastructure/Email/` e `FakeEmailSender` nos testes, conforme research R6
- [ ] T017 [P] Configurar Hangfire (PostgreSQL) e `ITenantJobRunner` (`BeginSystemScope` por Espaço) em `backend/src/FinMy.Infrastructure/Jobs/` e o job diário de limpeza de refresh tokens expirados e tokens de redefinição vencidos; teste de que o job roda sob o filtro do Espaço correto e grava `*_via = system`
- [ ] T018 Criar a casca do front em `frontend/src/app/`: `core/api` (cliente HTTP com base `/api/v1`), `core/errors` (ProblemDetails → mensagem pt-BR do `detail`), `core/auth` (`AuthService` com access token só em memória, refresh na abertura do app e `AuthInterceptor` com refresh único e fila em 401 `token_expired`, logout em `session_revoked`, research R4), guards de rota pública/logada, `shell/` (barra inferior no celular e menu lateral ≥ 1024 px, nome do Espaço no topo), `features/mes/` placeholder, e PWA (`@angular/pwa`, `manifest.webmanifest` "Fin-My", service worker só para o app shell), conforme contracts/ui-rotas.md; testes de unidade do interceptor (refresh único com requisições simultâneas)

**Checkpoint**: fundação pronta — interceptors e filtros provados com sondas, API sobe com `/health`,
autenticação e casca do front prontas para as telas.

---

## Phase 3: User Story 1 — Criar conta e começar a usar (Priority: P1) 🎯 MVP

**Goal**: criar conta com nome, e-mail e senha e entrar num Espaço vazio próprio.

**Independent Test**: criar uma conta nova e ver o app logado com "Finanças de <primeiro nome>" (spec US1).

- [ ] T019 [US1] Implementar o caso de uso `SignUp` em `backend/src/FinMy.Application/Auth/` (validação de `User.name`, `User.email` e senha conforme data-model.md e spec FR-001/FR-002; normalização de `User.email_normalized`; nome do Espaço por `TenantNaming` em `backend/src/FinMy.Domain/Tenants/` conforme research R10; `User` + `Tenant` + `TenantMember` (`role = owner`) na mesma transação; violação de unicidade → 409 `email_already_registered`, research R7) e o endpoint `POST /api/v1/auth/signup` em `backend/src/FinMy.Api/Endpoints/Auth/` conforme contracts/openapi.yaml; testes de API dos cenários 1.1–1.3 e dos Edge Cases de e-mail (maiúsculas/espaços e duas criações simultâneas)
- [ ] T020 [US1] Criar a tela `/criar-conta` em `frontend/src/app/features/auth/criar-conta/` (campos, mostrar/ocultar senha, mensagens do cenário 1.2 com atalhos para entrar e recuperar senha, requisito de 8 caracteres) e o teste e2e `frontend/e2e/criar-conta.spec.ts` (mobile e desktop)

**Checkpoint**: US1 funcional e testável sozinha.

---

## Phase 4: User Story 2 — Entrar e continuar logado no celular (Priority: P1)

**Goal**: entrar com e-mail e senha e continuar logado por até 90 dias sem uso; bloqueio após 5 erros.

**Independent Test**: entrar, fechar e reabrir o app e continuar logado; 6ª tentativa errada bloqueada (spec US2).

- [ ] T021 [US2] Implementar `LoginThrottleRule` (regras de research R5, sobre `LoginThrottle.failed_count`, `LoginThrottle.window_started_at`, `LoginThrottle.locked_until` de data-model.md) em `backend/src/FinMy.Domain/Auth/` com testes puros, o caso de uso `Login` em `backend/src/FinMy.Application/Auth/` (tempo constante com hash fantasma para e-mail inexistente; erro único `invalid_credentials`, FR-014; 429 `login_locked` com `retryAfterSeconds`) e o endpoint `POST /api/v1/auth/login` com o rate limiter `auth`; testes de API dos cenários 2.1, 2.2 e 2.5
- [ ] T022 [US2] Implementar `Refresh` (rotação; reuso de token rotacionado revoga a família com `RefreshToken.revoked_reason = reuse_detected`; validade deslizante de 90 dias em `RefreshToken.expires_at` conforme data-model.md) e `Logout` (idempotente, apaga o cookie) em `backend/src/FinMy.Application/Auth/` e endpoints `POST /api/v1/auth/refresh` e `POST /api/v1/auth/logout`; testes dos cenários 2.3 e 2.4 com relógio controlado
- [ ] T023 [US2] Implementar `GET /api/v1/me` em `backend/src/FinMy.Api/Endpoints/Me/` (usuário e Espaço do contexto, contracts/openapi.yaml `MeResponse`) e a tela `/entrar` em `frontend/src/app/features/auth/entrar/` (erro único, aviso de bloqueio com tempo restante, retorno à rota pedida após login) e o e2e `frontend/e2e/entrar.spec.ts` (inclui fechar/reabrir o contexto do navegador e continuar logado)

**Checkpoint**: US1 + US2 funcionando juntas; app usável no celular sem relogar.

---

## Phase 5: User Story 3 — Isolamento entre Espaços (Priority: P1)

**Goal**: provar que nada de um Espaço aparece em outro.

**Independent Test**: com duas contas, nenhum dado de A é visível ou alterável por B (spec US3, SC-003).

- [ ] T024 [US3] Criar a suíte de isolamento em `backend/tests/FinMy.Api.Tests/Isolation/`: (a) descoberta por reflexão de todos os tipos `ITenantEntity` mapeados, exigindo uma fábrica de dados por tipo (falha se faltar — nesta feature só as sondas, no contexto de teste); (b) para cada operação do OpenAPI gerado pela API, chamadas como B após dados criados por A, verificando ausência de dados de A, 404 `not_found` em ids de A e nada gravado; (c) teste de que o `tid` do token só é emitido para quem tem `TenantMember` (arquitetura §3.6); incluir o comando no README de testes

**Checkpoint**: SC-003 coberto e a suíte cresce sozinha com as próximas features.

---

## Phase 6: User Story 4 — Recuperar senha (Priority: P2)

**Goal**: receber link por e-mail e definir nova senha.

**Independent Test**: pedir recuperação, usar o link, entrar com a nova senha (spec US4).

- [ ] T025 [US4] Implementar `RequestPasswordReset` (resposta 202 idêntica exista ou não a conta, FR-011; `PasswordResetToken` conforme data-model.md; e-mail pt-BR com `{APP_URL}/redefinir-senha?token=`) e `ConfirmPasswordReset` (troca a senha, renova `User.security_stamp`, marca `PasswordResetToken.used_at`, revoga todas as sessões com `RefreshToken.revoked_reason = password_reset`; token inválido/expirado/usado → 410 `reset_token_invalid`) em `backend/src/FinMy.Application/Auth/` e os endpoints de contracts/openapi.yaml; testes dos cenários 4.1–4.4 com `FakeEmailSender`
- [ ] T026 [US4] Criar as telas `/esqueci-senha` e `/redefinir-senha` em `frontend/src/app/features/auth/` e o e2e `frontend/e2e/recuperar-senha.spec.ts` lendo o e-mail pela API do Mailpit

**Checkpoint**: recuperação de senha completa.

---

## Phase 7: User Story 5 — Perfil e sair (Priority: P3)

**Goal**: alterar nome, senha e nome do Espaço; sair do aparelho.

**Independent Test**: alterar nome e senha, sair, entrar com a nova senha (spec US5).

- [ ] T027 [US5] Implementar `UpdateMyName` (`PATCH /api/v1/me`, `User.name` conforme data-model.md), `ChangePassword` (`POST /api/v1/me/password`; senha atual errada → 422 `current_password_incorrect`; revoga as OUTRAS sessões com `RefreshToken.revoked_reason = password_changed`) e `RenameTenant` (`PATCH /api/v1/tenant`, `Tenant.name` conforme data-model.md) em `backend/src/FinMy.Application/Me/` e `backend/src/FinMy.Api/Endpoints/Me/`; testes dos cenários 5.1–5.3 e do Edge Case de dois aparelhos (o outro cai na próxima ação)
- [ ] T028 [US5] Criar a tela `/perfil` em `frontend/src/app/features/perfil/` (nome, nome do Espaço refletido no topo, trocar senha, sair) e o e2e `frontend/e2e/perfil.spec.ts` incluindo dois contextos de navegador para o Edge Case de troca de senha

**Checkpoint**: todas as user stories da 001 funcionando.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T029 [P] Revisar segurança: CSP, HSTS, `X-Content-Type-Options`, `Referrer-Policy` em `frontend/nginx.conf`; atributos do cookie `finmy_rt`; garantir por teste que logs não contêm e-mail nem senha (`backend/tests/FinMy.Api.Tests/Logging/`)
- [ ] T030 Executar o roteiro de quickstart.md §3 nos dois viewports, completar `frontend/e2e/` com o que faltar, escrever `README.md` de desenvolvimento (como subir, testar e migrar) e registrar desvios em `docs/arquitetura.md` se houver

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001–T006)**: T001 antes de T003-backend/T004/T005; T002 antes de T003-frontend; T004 antes do job `e2e` da T005.
- **Foundational (T007–T018)**: depende do Setup. Ordem interna: T007 → T008 → T009 → T010 → T012 → T013 → T014 → T015. T011, T016, T017 [P] após T010. T018 após T013 (precisa do formato de erro) e pode correr em paralelo com T014–T017.
- **US1 (T019–T020)**: após Foundational. T020 pode começar com o contrato e terminar após T019.
- **US2 (T021–T023)**: após US1 (os testes de login usam contas criadas por signup).
- **US3 (T024)**: após US2 (precisa de `/me` e de sessões).
- **US4 (T025–T026)** e **US5 (T027–T028)**: após US2; independentes entre si.
- **Polish (T029–T030)**: após todas.

### Parallel Opportunities

- T002, T003 (frontend), T006 em paralelo com T001.
- T011, T016, T017 em paralelo depois de T010.
- T018 (front) em paralelo com T014–T017 (back).
- Em cada história, a tarefa de front pode começar a partir do contrato enquanto a de back é feita.
- US4 e US5 em paralelo.

## Parallel Example: Foundational

```text
Depois de T010:
  T011 testes de arquitetura
  T016 e-mail
  T017 Hangfire + ITenantJobRunner
Em paralelo com T014–T017:
  T018 casca do front
```

## Implementation Strategy

### MVP desta feature

Setup → Foundational → US1 → US2: já é possível criar conta e usar o app logado no celular.
US3 fecha a garantia de isolamento (obrigatória antes de qualquer dado financeiro, na 002).

### Entrega incremental

Cada tarefa vira um PR pequeno com testes; a `main` sempre verde. Ao fim da fase 5 (US3), a 002 pode
começar mesmo com US4/US5 em andamento.

## Notes

- Citações de campos (`Entity.field`) referem-se a [data-model.md](data-model.md); regras de API a
  [contracts/openapi.yaml](contracts/openapi.yaml); telas a [contracts/ui-rotas.md](contracts/ui-rotas.md).
- Nenhuma tarefa atribui `TenantId` ou campos de auditoria manualmente (constituição T1).
- Mensagens visíveis sempre em pt-BR.
