# Fin-My

App de finanças pessoais (PWA) que substitui a planilha "Planejamento Financeiro". Lançamento rápido
pelo celular, visão completa no computador, cartões com faturas e parcelas, tags de acerto,
planejamento do ano e integração com o Claude.

Stack: .NET 10 · PostgreSQL 17 · Angular (PWA) · Docker Compose.

## Onde está o quê

| O quê | Onde |
|---|---|
| Specs por feature (spec-kit): `spec.md`, `plan.md`, `tasks.md`, `quickstart.md`, `contracts/` | [`specs/`](specs/README.md) (001 a 004 e 008 prontas; a próxima é a [`005-recorrencias`](specs/005-recorrencias/)) |
| Constituição: princípios, glossário, regras transversais | [`.specify/memory/constitution.md`](.specify/memory/constitution.md) |
| Arquitetura técnica (normativa) e desvios registrados | [`docs/arquitetura.md`](docs/arquitetura.md) |
| Modelo de dados | [`docs/modelo-de-dados.md`](docs/modelo-de-dados.md) |
| Backend (`FinMy.Api`, `Application`, `Domain`, `Infrastructure`) e testes | [`backend/`](backend/) · guia dos testes em [`backend/tests/README.md`](backend/tests/README.md) |
| Front-end Angular e e2e (Playwright) | [`frontend/`](frontend/) · e2e em [`frontend/e2e/`](frontend/e2e/README.md) |
| Integração com o Claude (conector MCP em `/mcp`, skill, publicação com HTTPS e roteiro de testes) | [`integrations/claude/`](integrations/claude/README.md) |
| Vetores de teste compartilhados (front e back) | [`shared/`](shared/README.md) |
| CI | [`.github/workflows/ci.yml`](.github/workflows/ci.yml) |

## Pré-requisitos

- Docker com Compose v2 (ou Podman, veja [Podman](#podman))
- .NET SDK 10 e Node 24 (só para rodar testes e o front fora dos containers)
- `cp .env.example .env` na raiz

## Subir tudo

```bash
cp .env.example .env
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

Sobe `db` (PostgreSQL), `migrate` (aplica as migrações e termina), `api` e `web` (nginx com o build do
Angular e proxy de `/api` e `/mcp` para a `api`).

| Serviço | URL |
|---|---|
| App | <http://localhost:8080> (`WEB_PORT`) |
| Saúde da API | <http://localhost:8080/health> → `200` |
| Mailpit (e-mails de recuperação de senha, só dev) | <http://localhost:8025> |
| PostgreSQL (só dev) | `localhost:5432` |

Só o `web` é publicado pelo compose base; Mailpit e a porta do Postgres vêm do
`docker-compose.dev.yml`. O Mailpit captura todo e-mail enviado pela API: é onde aparece o link de
"Esqueci minha senha".

### Variáveis do `.env`

Valores de desenvolvimento em [`.env.example`](.env.example); troque todos em produção.

| Variável | Para quê |
|---|---|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Banco criado pelo serviço `db` |
| `ConnectionStrings__Default` | Conexão usada por `api` e `migrate` (mantenha igual às credenciais acima) |
| `Jwt__SigningKey` | Chave de assinatura do JWT (mín. 32 caracteres; `openssl rand -base64 48`) |
| `Email__Host`, `Email__Port`, `Email__User`, `Email__Password`, `Email__UseTls` | SMTP (em dev, o Mailpit) |
| `Email__FromAddress`, `Email__FromName` | Remetente dos e-mails |
| `APP_URL` | URL pública do app; usada nos links dos e-mails (`{APP_URL}/redefinir-senha?token=...`) |
| `WEB_PORT` | Porta do `web` no host (padrão 8080) |
| `RateLimiting__Auth__PermitLimit` | Requisições por minuto e por IP nos endpoints de auth (padrão 20); só o override de CI sobe o limite |
| `Hangfire__Enabled` | `false` desliga os jobs em segundo plano (usado nos testes HTTP; veja [Desvios](docs/arquitetura.md#14-desvios-e-decisões-de-implementação-da-001)) |

### Recomeçar do zero

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v   # apaga também o volume do banco
```

## Testes

### Backend

Quatro projetos, um por camada. Rode **um por vez** (cada um sobe um PostgreSQL via Testcontainers,
então precisa de Docker ou do socket do Podman ativo):

```bash
cd backend
dotnet build
dotnet test tests/FinMy.Domain.Tests
dotnet test tests/FinMy.Application.Tests
dotnet test tests/FinMy.Architecture.Tests
dotnet test tests/FinMy.Api.Tests
dotnet format --verify-no-changes   # o CI recusa código sem formatar
```

Mais detalhes (suíte de isolamento entre Espaços, como acrescentar entidades e endpoints) em
[`backend/tests/README.md`](backend/tests/README.md).

### Front-end

```bash
cd frontend
npm ci
npm run lint
npm test            # unidade (sem watch)
npm run build
npm run format      # prettier
```

### E2E (Playwright, 360 px e 1280 px)

Dois projetos: `mobile` (360×800) e `desktop` (1280×800). Sem `E2E_BASE_URL`, sobe o `ng serve` na
porta 4200 e só roda o que usa API simulada. Os testes contra a **stack real** e os de PWA exigem a
stack do compose no ar e `E2E_BASE_URL` (senão são ignorados):

```bash
docker compose -f docker-compose.yml -f docker-compose.ci.yml up -d --build --wait
cd frontend
npx playwright install chromium     # uma vez
E2E_BASE_URL=http://localhost:8080 npm run e2e
```

`docker-compose.ci.yml` publica o Mailpit (API em `:8025`; o e2e lê o e-mail de recuperação por ele,
`MAILPIT_URL` muda o endereço) e sobe o limite de requisições de auth, que derrubaria execuções
seguidas. O roteiro manual do quickstart e a especificação de qual e2e cobre cada passo estão em
[`frontend/e2e/README.md`](frontend/e2e/README.md).

## Rotas do app e da API

Telas (Angular; as públicas são `/entrar`, `/criar-conta`, `/esqueci-senha` e `/redefinir-senha`):

| Rota | Tela |
|---|---|
| `/mes/:mes` (`/` e `/mes` abrem o mês atual) | Visão do mês: resumo Previsto x Realizado, lançamentos, transferências, filtro por carteira e pendências |
| `/carteiras`, `/carteiras/nova`, `/carteiras/:id` | Lista, cadastro e edição de carteiras (arquivar, tornar principal, excluir) |
| `/cartoes`, `/cartoes/novo`, `/cartoes/:id/editar`, `/cartoes/:id/faturas/:mes` | Lista de cartões com fatura atual, próxima e limite; cadastro e edição; tela da fatura (compras, pagamentos, levar restante) |
| `/busca` | Busca de lançamentos e transferências por texto, sem acento nem caixa (e por tag) |
| `/acertos`, `/acertos/:mes` | Acertos do mês por tag de acerto: Gasto, Cobrado, Recebido, A cobrar e Saldo acumulado; "Gerar cobrança" |
| `/tags` | Gerenciar tags: criar, renomear, cor, marcar como de acerto, arquivar, excluir e juntar |
| `/perfil` | Nome do usuário e do Espaço, troca de senha, sair |

API (`/api/v1`, tudo com sessão; contrato em
[`specs/002-carteiras-e-lancamentos/contracts/openapi.yaml`](specs/002-carteiras-e-lancamentos/contracts/openapi.yaml) e [`specs/003-cartoes-e-faturas/contracts/openapi.yaml`](specs/003-cartoes-e-faturas/contracts/openapi.yaml) e [`specs/004-tags-e-acertos/contracts/openapi.yaml`](specs/004-tags-e-acertos/contracts/openapi.yaml)).
Os `POST` de criação aceitam `Idempotency-Key`:

| Recurso | Rotas |
|---|---|
| Carteiras | `GET/POST /wallets`, `GET/PUT/DELETE /wallets/{id}`, `POST /wallets/{id}/make-primary`, `/archive`, `/unarchive` |
| Lançamentos | `POST /entries`, `GET/PUT/DELETE /entries/{id}`, `PATCH /entries/{id}/status`, `POST /entries/{id}/duplicate`, `POST /entries/bulk`, `GET /entries/pending`, `/suggestions`, `/search` |
| Transferências | `POST /transfers`, `GET/PUT/DELETE /transfers/{id}` |
| Cartões | `GET/POST /credit-cards`, `GET/PUT/DELETE /credit-cards/{id}`, `POST /credit-cards/{id}/archive`, `/unarchive`, `GET /credit-cards/{id}/invoice-for?date=` |
| Faturas | `GET /credit-cards/{id}/invoices/{yyyy-MM}`, `POST/DELETE .../payments`, `POST .../carry-over`, `PUT/DELETE /invoice-payments/{id}` |
| Compras no cartão | `POST /card-purchases`, `GET/PUT/DELETE /card-purchases/{id}`, `PATCH /card-purchases/{id}/reconciled`, `POST /card-purchases/{id}/move`, `PUT /installment-plans/{id}` |
| Mês | `GET /months/{yyyy-MM}?walletIds=&tagIds=` (inclui as faturas com vencimento no mês) |
| Tags | `GET/POST /tags`, `PUT/DELETE /tags/{id}`, `POST /tags/{id}/archive`, `/unarchive`, `/merge` |
| Acertos | `GET /settlements/{yyyy-MM}`, `GET /settlements/{yyyy-MM}/tags/{tagId}/items` |
| Dividir | `POST /entries/split`, `POST /card-purchases/split` |
| Desfazer exclusão | `POST /deletions/{batchId}/restore` (até 10 min) |

## Migrações (EF Core)

As migrações ficam em `backend/src/FinMy.Infrastructure/Persistence/Migrations/` e são aplicadas por
um passo explícito, `dotnet FinMy.Api.dll migrate` (serviço `migrate` do compose, que a `api` aguarda).
A API **não** migra sozinha ao subir.

```bash
cd backend
dotnet tool install --global dotnet-ef   # uma vez

# criar a migração depois de mudar entidades/configurações (não conecta ao banco)
dotnet ef migrations add NomeDaMigracao \
  -p src/FinMy.Infrastructure -s src/FinMy.Api -o Persistence/Migrations

# aplicar no banco do compose dev (porta 5432 exposta), sem passar pelo container `migrate`
FINMY_DESIGN_CONNECTION="Host=localhost;Database=finmy;Username=finmy;Password=finmy_dev_password" \
  dotnet ef database update -p src/FinMy.Infrastructure -s src/FinMy.Api
```

Para aplicar pelo caminho de produção: `docker compose run --rm migrate`.

## Podman

O CI usa Docker; localmente dá para usar Podman:

- Testes do backend: ative o socket (`systemctl --user enable --now podman.socket`) e exporte
  `DOCKER_HOST=unix:///run/user/$UID/podman/podman.sock` e `TESTCONTAINERS_RYUK_DISABLED=true`.
  Depois dos testes, limpe só os containers de teste já parados:
  `podman container prune -f --filter label=org.testcontainers=true`.
- Stack: `podman compose` delega a um provedor externo; o `podman-compose` (Python) falha com este
  compose em algumas versões (traceback logo ao subir). Com o provedor `docker-compose` (binário Go)
  o comando do compose deve funcionar; se não, suba a mão (rede própria, `db`, `migrate`, `api`,
  `web`) como abaixo. O nginx do `web` resolve o host `api` ao iniciar: reinicie o `web` se recriar a `api`.

```bash
podman network create finmy
podman build -t finmy-api -f backend/Dockerfile backend
podman build -t finmy-web -f frontend/Dockerfile frontend
podman run -d --name finmy-db --network finmy --network-alias db \
  -e POSTGRES_DB=finmy -e POSTGRES_USER=finmy -e POSTGRES_PASSWORD=finmy_dev_password docker.io/library/postgres:17
podman run -d --name finmy-mailpit --network finmy --network-alias mailpit -p 127.0.0.1:8025:8025 docker.io/axllent/mailpit:latest
podman run --rm --network finmy --env-file .env finmy-api migrate
podman run -d --name finmy-api --network finmy --network-alias api --env-file .env finmy-api
podman run -d --name finmy-web --network finmy -p 127.0.0.1:8080:80 finmy-web   # reinicie o web se recriar a api
```

## Convenções

- Uma tarefa por PR, citando a issue (`Closes #N`); testes escritos junto com o código.
- Build sem avisos, `dotnet format` e `npm run lint` sem pendências antes de abrir o PR.
- Mudança que contraria `docs/arquitetura.md` precisa atualizá-lo (veja o §14 para os desvios da 001).
