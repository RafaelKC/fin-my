# Implementation Plan: Lançamentos recorrentes

**Branch**: `005-recorrencias` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Recorrências mensais de Entrada, Saída e Compra que geram lançamentos reais até dezembro do ano
seguinte, com job diário que estende o horizonte; alteração e encerramento "a partir de um mês"
preservando ocorrências pagas e alteradas; "Repetir todo mês" a partir de um lançamento. Decisões em
[research.md](research.md); contrato em [contracts/api.md](contracts/api.md).

## Technical Context

Igual às anteriores. Pré-requisito: 004 na `main` (tags) e 003 (compras/faturas).

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| I | Mês novo já nasce com as contas fixas (SC-001) | ✅ |
| III | `RecurrenceScheduler` puro; ocorrências são lançamentos comuns (sem cálculo especial) | ✅ |
| IV | Elegibilidade única (research R4) usada por alterar, encerrar e excluir | ✅ |
| T1 | Job usa `ITenantJobRunner` (`created_via = system`) | ✅ |

## Project Structure

```text
backend/src/FinMy.Domain/Recurrences/
backend/src/FinMy.Application/Recurrences/   RecurrenceGenerator, handlers
backend/src/FinMy.Infrastructure/Jobs/       RecurrenceHorizonJob
backend/src/FinMy.Api/Endpoints/Recurrences/
frontend/src/app/features/recorrencias/
```

## Complexity Tracking

Nenhuma violação.
