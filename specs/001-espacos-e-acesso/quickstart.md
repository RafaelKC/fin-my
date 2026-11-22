# Quickstart — validar a 001

Guia para provar que a feature (e a fundação) funciona. Detalhes de contrato em
[`contracts/openapi.yaml`](contracts/openapi.yaml) e telas em [`contracts/ui-rotas.md`](contracts/ui-rotas.md).

## Pré-requisitos

- Docker com Compose v2
- .NET SDK 10 e Node LTS (só para rodar testes fora do Docker)
- `cp .env.example .env` na raiz

## 1. Subir tudo

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

Esperado:
- `db` saudável; `api` roda `migrate` e sobe; `web` responde em `http://localhost:8080`.
- `GET http://localhost:8080/health` → 200.
- Mailpit (só dev) em `http://localhost:8025`.

## 2. Testes automatizados

```bash
cd backend && dotnet test           # domínio, aplicação (Testcontainers), API, arquitetura
cd frontend && npm ci && npm test   # unidade
cd frontend && npx playwright test  # e2e (com o compose do passo 1 rodando)
```

Precisam passar, no mínimo:

| Suíte | Prova |
|---|---|
| `Infrastructure/TenantInterceptorTests` (sondas, research R1) | tenant atribuído sozinho; gravar sem tenant falha; trocar tenant falha; ler sem contexto não retorna nada |
| `Infrastructure/AuditInterceptorTests` | `created_*`/`updated_*`/`*_via` preenchidos sozinhos; `created_*` não muda em update |
| `Infrastructure/SoftDeleteTests` | `Remove` vira soft delete com lote; filtro esconde; `IgnoreQueryFilters(["SoftDelete"])` mantém o filtro de Espaço (research R2) |
| `Infrastructure/CompositeForeignKeyTests` | o banco recusa referência de um Espaço para outro |
| `Architecture/*` | camadas; toda entidade é `ITenantEntity` ou global listada; sem `IgnoreQueryFilters()` sem nome; sem `DateTime.Now` |
| `Api/Auth/*` | cenários US1–US5 e Edge Cases da spec |
| `Api/Isolation/*` | contas A e B: nada de A visível para B em `/me`, `/tenant` (SC-003) |

## 3. Roteiro manual (celular 360 px e computador)

1. `/criar-conta` → "Ana", `ana@exemplo.com`, `segredo123` → entra e vê "Finanças de Ana" (US1.1).
2. Repetir com o mesmo e-mail em maiúsculas → mensagem "Este e-mail já tem conta…" (US1.2, Edge).
3. Sair, entrar com senha errada 5× → na 6ª, aviso de bloqueio de 15 min (US2.5).
4. Entrar num 2º navegador; no 1º, trocar a senha em `/perfil` → a próxima ação no 2º volta ao login (Edge, research R3).
5. `/esqueci-senha` com o e-mail → link chega no Mailpit; usar → nova senha funciona; usar o link de novo → "link expirou" (US4).
6. Com e-mail inexistente em `/esqueci-senha` → mesma mensagem e nenhum e-mail no Mailpit (US4.2).
7. Fechar e reabrir o navegador → continua logado (US2.3).
8. Instalar como PWA no celular (Adicionar à tela inicial) → abre em tela cheia já logado.

## 4. Pronto quando

- Todos os testes do passo 2 passam no CI.
- O roteiro do passo 3 funciona nos dois tamanhos de tela.
- `docker compose up` em máquina limpa sobe o sistema sem passos manuais além do `.env`.
