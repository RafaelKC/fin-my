# Data model — 003

Normativo: `docs/modelo-de-dados.md` seções `credit_cards`, `invoices`, `installment_plans`,
`card_purchases`, `invoice_payments` — implementar exatamente aquelas colunas, com estes ajustes:

- `invoices`: o único passa a ser `UNIQUE (tenant_id, credit_card_id, due_month)` (o `ON CONFLICT` de
  research R2 precisa do `tenant_id`). `estimate_cents` só entra na 007.
- `card_purchases`: nesta feature **sem** `settlement_tag_id`, `split_group_id` e `recurrence_*`
  (vêm na 004/005). `origin` nesta feature: `manual, installment, carry_over`. Valores negativos não
  existem (`amount_cents > 0 AND <= 999999999`).
- `credit_cards`: `name_normalized` único parcial como `wallets`; `closing_day`/`due_day`
  `CHECK BETWEEN 1 AND 31`; `limit_cents CHECK >= 0 AND <= 999999999`; `color` com o mesmo `CHECK`
  de `wallets`.
- `invoice_payments`: `CHECK (amount_cents > 0)`; `payment_date` intervalo RT-03.
- Todas as FKs entre essas tabelas e `wallets` são compostas (`HasTenantForeignKey`). A FK de
  `card_purchases → invoices` é a tripla `(tenant_id, invoice_id, credit_card_id)` com o índice
  `UNIQUE (tenant_id, id, credit_card_id)` em `invoices`.

Migração: `CreditCards`.

## Domínio (`FinMy.Domain/Cards/`)

- `CreditCard` (`Create`, `Rename`, `ChangeDays`, `ChangePaymentWallet`, `ChangeLimit`, `Archive`,
  `Unarchive`), `Invoice` (datas; sem totais gravados), `InstallmentPlan`, `CardPurchase`
  (`DisplayDescription` = base + " k/N" quando parcela), `InvoicePayment`; enums `PurchaseNature`,
  `PurchaseOrigin`, `InstallmentMode`, `InvoiceStatus`.
- Regras puras: `InvoiceCycle`, `InvoiceStatusRule`, `InstallmentGenerator` (research R1).
