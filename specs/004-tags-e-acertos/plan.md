# Implementation Plan: Tags e acerto de contas

**Branch**: `004-tags-e-acertos` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Tags livres em Entradas, Saídas e Compras (no máximo uma de acerto por lançamento), escolhidas com um
toque ou criadas no formulário; tela de Acertos por mês com Gasto, Cobrado, Recebido, A cobrar, A
receber e Saldo acumulado; "Gerar cobrança"; divisão de um lançamento entre pessoas; filtros por tag
e subtotais por pessoa na fatura; gestão de tags (renomear, junção, marcar acerto). Decisões em
[research.md](research.md).

## Technical Context

Igual à 001–003. Pré-requisito: 003 concluída na `main`.

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| I | 1 toque para tag frequente (SC-001), cobrança pré-preenchida, Dividir no formulário | ✅ |
| II | `tags` e ligações com FKs compostas para os dois lados | ✅ |
| III | `SettlementCalculator` e `SplitPlanner` puros; exemplo US2 (SC-002) e SC-004 automatizados; divisão usa `MoneySplitter` (RT-02) | ✅ |
| IV | Interpretações em research R1 (tag arquivada), R5 (divisão com parcela), R6 (filtro não muda resumo) | ✅ |
| V | Gerar cobrança é só front (nada salvo sem confirmação, FR-010) | ✅ |

## Project Structure

```text
backend/src/FinMy.Domain/{Tags,Settlements,Splits}/
backend/src/FinMy.Application/{Tags,Settlements}/   (+ ajustes em Entries/ e Cards/)
backend/src/FinMy.Api/Endpoints/{Tags,Settlements}/
frontend/src/app/shared/tag-picker/
frontend/src/app/features/{acertos,tags}/
frontend/src/app/features/lancamento/ (Tags, Dividir)
```

## Complexity Tracking

Nenhuma violação.
