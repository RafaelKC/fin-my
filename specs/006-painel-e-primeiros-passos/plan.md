# Implementation Plan: Painel anual, resumo de agora e primeiros passos

**Branch**: `006-painel-e-primeiros-passos` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Painel do ano (aba Home da planilha) com Previsto/Realizado e gráfico, "Agora" (saldos, faturas
próximas, pendências, acertos), guia de primeiros passos e exportação CSV. Sem regra de cálculo nova.
Decisões em [research.md](research.md); contrato em [contracts/api.md](contracts/api.md).

## Technical Context

Igual às anteriores. Pré-requisito: 005 na `main`.

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| III | Mesmo calculador da visão do mês; teste de paridade SC-001 | ✅ |
| V | Gráfico em SVG próprio, sem biblioteca | ✅ |

## Project Structure

```text
backend/src/FinMy.Application/{Panel,Now,Onboarding,Exports}/
backend/src/FinMy.Api/Endpoints/{Panel,Now,Onboarding,Exports}/
frontend/src/app/features/{painel,primeiros-passos}/   shared/chart-anual/
```

## Complexity Tracking

Nenhuma violação.
