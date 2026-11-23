# Implementation Plan: Carteiras, Entradas, Saídas e visão do mês

**Branch**: `002-carteiras-e-lancamentos` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Cadastro de Carteiras, lançamento rápido de Entradas/Saídas/Transferências pelo celular (valor com
calculadora, sugestões de descrição), visão do mês com resumo Previsto x Realizado e saldo levado ao
mês seguinte, pendências de meses anteriores, edição/exclusão com desfazer, duplicação e busca. Traz
também as peças de fundação adiadas da 001: `Money` + parser de expressões com vetores
compartilhados, `Idempotency-Key`, restaurar exclusão e o teclado numérico do front. Decisões em
[research.md](research.md).

## Technical Context

Igual à 001 (`specs/001-espacos-e-acesso/plan.md`): .NET 10 / EF Core 10 / PostgreSQL 17 / Angular 22
+ Material 3, xUnit + Shouldly + Testcontainers, Vitest + Playwright (360×800 e 1280×800).

**Performance Goals**: `GET /months/{m}` p95 < 300 ms com 5.000 lançamentos no Espaço; abrir o
formulário rápido < 300 ms; salvar < 1 s em 4G.

**Constraints**: todos os cálculos de negócio no servidor (constituição); o front só pré-visualiza o
valor com a porta TS do parser (vetores compartilhados); sem dados offline.

**Scale/Scope**: 3 tabelas, ~20 endpoints, 5 telas/sobreposições.

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| I. Tão fácil quanto a planilha | Formulário rápido com padrões (FR-010), sugestões, calculadora, "Salvar e lançar outro"; e2e mede SC-001 (≤ 4 toques) | ✅ |
| II. Isolamento | Tabelas são `TenantEntity` com FKs compostas; suíte de isolamento cobre os endpoints novos automaticamente (descoberta por OpenAPI) | ✅ |
| III. Exatidão monetária | `long` centavos, parser RT-05 com vetores compartilhados, `MonthSummaryCalculator` testado com o exemplo da US3 e o teste de consistência SC-003 | ✅ |
| IV. Regras explícitas | Interpretações registradas: R4 (linha de saldo inicial), R6 (exclusão de carteira), R7 (RT-04 no PUT), R8 (sugestões), R9 (pendências), R10 (ordem da busca) | ✅ |
| V. Simplicidade | Sem totais desnormalizados; agregação por SQL + composição no domínio | ✅ |
| T1 | Nenhum handler grava tenant/auditoria; `idempotency_keys` é a única exceção global (data-model) | ✅ |
| T2 | Regras puras (`ValueExpressionParser`, `MoneySplitter`, `TextNormalizer`, `MonthSummaryCalculator`, `Entry.ChangeDate`) com testes e vetores | ✅ |
| T3 | [contracts/openapi.yaml](contracts/openapi.yaml), [contracts/ui-rotas.md](contracts/ui-rotas.md) | ✅ |
| T4 | Concorrência otimista por `version` (R11) | ✅ |

Pós-design: sem violações. Ajustes no modelo normativo: `transfers` ganha `description_normalized` e
`notes` (busca e paridade com entries) — atualizar `docs/modelo-de-dados.md` na T019.

## Project Structure

```text
specs/002-carteiras-e-lancamentos/
├── plan.md  research.md  data-model.md  quickstart.md  tasks.md
└── contracts/ openapi.yaml  ui-rotas.md

shared/test-vectors/ value-expression.json  money-split.json  text-normalize.json

backend/src/
├── FinMy.Domain/        Money/  Wallets/  Entries/  Transfers/  Months/
├── FinMy.Application/   Wallets/  Entries/  Transfers/  Months/  Deletions/
├── FinMy.Infrastructure/Persistence/Configurations/ (Wallet, Entry, Transfer, IdempotencyKey)
│                        Persistence/Migrations/ (Wallets)  Jobs/ (expurgos)
└── FinMy.Api/           Endpoints/{Wallets,Entries,Transfers,Months,Deletions}/  Idempotency/
backend/tests/           …/Money, …/Wallets, …/Entries, …/Months (unidade e HTTP)

frontend/src/app/
├── core/money/          parser.ts, normalize.ts, brl.pipe.ts, month.ts (+ specs com vetores)
├── core/prefs/          device-prefs.service.ts
├── shared/amount-input/ teclado numérico + campo de valor
├── features/carteiras/  lista, formulário
├── features/lancamento/ formulário rápido (sheet/diálogo), sugestões
├── features/mes/        visão do mês, pendências
└── features/busca/
frontend/e2e/            carteiras, lancamento, mes (exemplo US3), pendencias, transferencias, busca
```

## Ordem e paralelismo

Fundação (T001–T005) → APIs por história → telas. Duas trilhas em paralelo (back e front) sempre que
as dependências permitem; ver tasks.md.

## Complexity Tracking

Nenhuma violação.
