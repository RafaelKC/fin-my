# Implementation Plan: Contas de usuário e Espaços isolados (+ fundação técnica)

**Branch**: `001-espacos-e-acesso` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-espacos-e-acesso/spec.md`

## Summary

Criar conta, entrar (com sessão de 90 dias no celular), recuperar/trocar senha, perfil e Espaço
isolado criado no cadastro. Por ser a primeira feature, entrega também a **fundação técnica** usada
por todas as outras: monorepo backend/frontend, camadas .NET, contexto ambiente, filtros nomeados e
interceptors que aplicam Espaço, auditoria e soft delete automaticamente, FKs compostas por Espaço,
testes de isolamento e arquitetura, casca do app Angular PWA, Dockerfiles, docker-compose e CI.
Abordagem detalhada em [research.md](research.md); normas em `docs/arquitetura.md`.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (LTS); TypeScript com Angular (última versão estável)

**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core 10 + Npgsql + EFCore.NamingConventions,
`Microsoft.AspNetCore.Authentication.JwtBearer`, `PasswordHasher<T>` (Identity Core), FluentValidation,
MailKit, Serilog; Angular Material 3, `@angular/pwa`

**Storage**: PostgreSQL 17 (tabelas desta feature: [data-model.md](data-model.md))

**Testing**: xUnit + Shouldly + Testcontainers.PostgreSql + NetArchTest.Rules (backend); runner padrão
do Angular CLI (unidade) + Playwright (e2e, viewports 360×800 e 1280×800)

**Target Platform**: servidor Linux em containers (Docker Compose); navegadores móveis (Chrome
Android, Safari iOS 16.4+) e desktop, instalável como PWA

**Project Type**: aplicação web (API + SPA/PWA) em monorepo

**Performance Goals**: endpoints de auth p95 < 300 ms (exceto o custo proposital do hash de senha);
abrir o app já logado (refresh + casca) < 2 s em 4G

**Constraints**: sem dados offline; refresh token só em cookie HttpOnly; nenhum e-mail ou dado
financeiro em logs; uma instância de API (research R3)

**Scale/Scope**: poucos usuários (uso doméstico), 6 telas no front, ~10 endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| I. Tão fácil quanto a planilha | Sessão de 90 dias (US2.3), app abre direto logado; casca mobile-first | ✅ |
| II. Isolamento total | Filtro `Tenant` fail-closed + TenantInterceptor + FKs compostas + suíte de isolamento; 404 para dado de outro Espaço | ✅ |
| III. Exatidão monetária | Sem dinheiro nesta feature; convenções (`long` centavos, `Money`) criadas na fundação para a 002 | ✅ (n/a) |
| IV. Regras explícitas e únicas | Regras de bloqueio, sessão e nome do Espaço registradas em research (R3, R5, R10); nada decidido em silêncio | ✅ |
| V. Simplicidade | Sem MediatR, sem NgRx, sem RLS na v1; idempotência e restaurar exclusão adiados para a 002 (primeiros dados com POST de criação e soft delete) | ✅ |
| T1. Espaço/auditoria pela infraestrutura | Interceptors + filtros nomeados; setters internos; teste de arquitetura | ✅ |
| T2. Regras puras e testadas | Regras de bloqueio/nome de Espaço em classes puras com relógio injetável; testes em Postgres real | ✅ |
| T3. Contrato explícito | [contracts/openapi.yaml](contracts/openapi.yaml) (validado) + [contracts/ui-rotas.md](contracts/ui-rotas.md) | ✅ |
| T4. Pronto para compartilhar | Autorização por participação (`tid` só emitido se existe `tenant_members`); restrições "1 membro" isoladas em dois índices únicos | ✅ |
| RT-01 | 404 `not_found` para id de outro Espaço | ✅ |
| RT-03/RT-10 | `IClock` com America/Sao_Paulo; front com `LOCALE_ID pt-BR` | ✅ |

**Pós-design (Phase 1)**: reavaliado após data-model e contratos — nenhuma violação. Uma ampliação
da arquitetura foi necessária (verificação de sessão por `sid`, research R3) e foi registrada em
`docs/arquitetura.md` §7.

## Project Structure

### Documentation (this feature)

```text
specs/001-espacos-e-acesso/
├── spec.md
├── plan.md              # este arquivo
├── research.md          # decisões R1–R10
├── data-model.md        # tabelas de identidade + infraestrutura base
├── quickstart.md        # como validar
├── contracts/
│   ├── openapi.yaml     # endpoints /auth, /me, /tenant, /health
│   └── ui-rotas.md      # rotas e telas do front
├── checklists/requirements.md
└── tasks.md             # gerado por /speckit-tasks
```

### Source Code (repository root)

```text
backend/
├── FinMy.slnx
├── Dockerfile
├── Directory.Build.props            # nullable, warnings como erro, versão .NET
├── src/
│   ├── FinMy.Domain/
│   │   ├── Common/                  # IEntity, ITenantEntity, IAudited, ISoftDeletable, IArchivable, TenantEntity, SystemUser, Money
│   │   ├── Users/                   # User, regras de nome/e-mail/senha
│   │   ├── Tenants/                 # Tenant, TenantMember, TenantRole, TenantNaming (R10)
│   │   └── Auth/                    # LoginThrottleRule (R5), SessionPolicy
│   ├── FinMy.Application/
│   │   ├── Abstractions/            # ICurrentContext, IClock, IEmailSender, IPasswordHasher, ITokenService, IAppDbContext
│   │   ├── Auth/                    # SignUp, Login, Refresh, Logout, RequestPasswordReset, ConfirmPasswordReset
│   │   └── Me/                      # GetMe, UpdateMyName, ChangePassword, RenameTenant
│   ├── FinMy.Infrastructure/
│   │   ├── Persistence/             # FinMyDbContext, ApplyFinMyConventions, Configurations/, Migrations/
│   │   ├── Persistence/Interceptors/# TenantInterceptor, SoftDeleteInterceptor, AuditInterceptor
│   │   ├── Context/                 # CurrentContext, CurrentContextMiddleware, SessionValidationMiddleware (R3)
│   │   ├── Auth/                    # JwtTokenService, PasswordHasherAdapter, RefreshTokenStore
│   │   ├── Email/                   # SmtpEmailSender, templates pt-BR
│   │   ├── Time/                    # SystemClock (America/Sao_Paulo)
│   │   └── Jobs/                    # Hangfire, ITenantJobRunner, limpeza de tokens
│   └── FinMy.Api/
│       ├── Program.cs               # composição, ProblemDetails, rate limiting, comando "migrate"
│       ├── Endpoints/Auth/  Endpoints/Me/
│       └── Errors/                  # mapeamento erro de domínio → ProblemDetails (code + detail pt-BR)
└── tests/
    ├── FinMy.Domain.Tests/
    ├── FinMy.Application.Tests/     # inclui Infrastructure/* com TestFinMyDbContext + sondas (R1)
    ├── FinMy.Api.Tests/             # Auth/*, Isolation/* (WebApplicationFactory + Testcontainers)
    └── FinMy.Architecture.Tests/

frontend/
├── Dockerfile
├── nginx.conf                       # SPA fallback + proxy /api e /mcp
├── angular.json  package.json  ngsw-config.json
├── src/
│   ├── app/
│   │   ├── core/                    # auth.service, auth.interceptor (R4), guards, problem-details → mensagens
│   │   ├── shell/                   # layout com barra inferior / menu lateral
│   │   ├── features/auth/           # entrar, criar-conta, esqueci-senha, redefinir-senha
│   │   ├── features/perfil/
│   │   └── features/mes/            # placeholder até a 002
│   ├── manifest.webmanifest
│   └── styles/
└── e2e/                             # Playwright

shared/test-vectors/                 # criado vazio com README (preenchido a partir da 002)
docker-compose.yml                   # db, api, web
docker-compose.dev.yml               # mailpit, portas de debug
.env.example
.github/workflows/ci.yml             # backend, frontend, e2e, docker
.editorconfig
```

**Structure Decision**: aplicação web em monorepo, conforme `docs/arquitetura.md` §1, com `backend/`
(4 projetos + 4 de teste) e `frontend/` (Angular), cada um com seu Dockerfile, e um único
`docker-compose.yml` na raiz.

## Escopo da fundação nesta feature (para `/speckit-tasks`)

Entram na 001 (bloqueiam todas as features): estrutura acima; `ICurrentContext`; filtros e
interceptors com testes por sondas; FKs compostas (convenção + teste); `IClock`; ProblemDetails;
Serilog; health check; migrações e comando `migrate`; Dockerfiles, compose e CI; casca do front com
auth e PWA; testes de arquitetura; suíte de isolamento (com as rotas da 001).

Ficam para a 002 (primeiro uso real): endpoint de restaurar exclusão (`/deletions/{batchId}/restore`),
middleware de `Idempotency-Key`, `Money`/parser de expressões com vetores compartilhados, teclado
numérico do front.

## Complexity Tracking

Nenhuma violação da constituição.
