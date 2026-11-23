# Implementation Plan: Cartões de crédito, Compras parceladas e Faturas

**Branch**: `003-cartoes-e-faturas` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Cartões com ciclo de fatura (fechamento/vencimento), compras à vista, parceladas e parcelamentos em
andamento, faturas calculadas (Total, Pago, Restante, Situação), pagamentos (total/parcial, caixinha
na visão do mês), "levar restante", conferência e mover compra, limite disponível e integração com o
resumo do mês (RT-07). Decisões em [research.md](research.md).

## Technical Context

Igual à 001/002. Depende da 002 inteira (parser, idempotência, desfazer, `MonthSummaryCalculator`,
formulário rápido, visão do mês, sugestões).

**Performance Goals**: `GET /credit-cards` e fatura p95 < 300 ms com 3 cartões e 3.000 compras.

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| I | Aba Cartão no formulário rápido com fatura pré-visualizada; parcelas automáticas (SC-003) | ✅ |
| II | 5 tabelas `TenantEntity` com FKs compostas; FK tripla compra→fatura garante mesmo cartão | ✅ |
| III | `InvoiceCycle`, `InvoiceStatusRule`, `InstallmentGenerator` puros com vetores compartilhados; SC-002 e SC-004 automatizados | ✅ |
| IV | Interpretações em research R2 (fatura sem linha), R3 (mudança de dias), R5 (confirmação), R8 (faturas zeradas fora da visão) | ✅ |
| V | Totais calculados, nunca gravados | ✅ |
| RT-04/RT-07 | Pagamento conta no mês de vencimento da fatura; compras nunca entram direto no resumo | ✅ |

## Project Structure

```text
shared/test-vectors/ invoice-cycle.json  invoice-status.json  installments.json
backend/src/FinMy.Domain/Cards/          entidades + InvoiceCycle, InvoiceStatusRule, InstallmentGenerator
backend/src/FinMy.Application/Cards/     cartões, compras, faturas, pagamentos
backend/src/FinMy.Api/Endpoints/Cards/
frontend/src/app/core/money/             invoice-cycle.ts, installments.ts (+ vetores)
frontend/src/app/features/cartoes/       lista, formulário, fatura
frontend/src/app/features/lancamento/    aba Cartão
frontend/src/app/features/mes/           linhas de fatura
```

## Complexity Tracking

Nenhuma violação.
