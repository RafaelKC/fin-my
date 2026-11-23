# Implementation Plan: Planejamento do ano

**Branch**: `007-planejamento-anual` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Grade anual estilo planilha (linhas por descrição, colunas por mês) que cria/edita/exclui Previstas
comuns; Estimativa de fatura que entra no Previsto até o fechamento; preencher meses; transformar em
recorrência; copiar do ano anterior. Decisões em [research.md](research.md); contrato em
[contracts/api.md](contracts/api.md).

## Technical Context

Igual às anteriores. Pré-requisito: 006 na `main`.

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| III | Totais da grade = painel (mesma fonte); `InvoiceForecast` puro com vetor; SC-002 e SC-003 automatizados | ✅ |
| IV | Regras de célula editável, data, carteira e cópia em research R2–R4 | ✅ |
| V | Linhas calculadas, nada guardado além da estimativa | ✅ |

## Project Structure

```text
backend/src/FinMy.Domain/Cards/InvoiceForecast.cs   shared/test-vectors/invoice-forecast.json
backend/src/FinMy.Application/Planning/
backend/src/FinMy.Api/Endpoints/Planning/
frontend/src/app/features/planejamento/
```

## Complexity Tracking

Nenhuma violação.
