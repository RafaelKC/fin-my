# Data model — 004

Normativo: `docs/modelo-de-dados.md` (`tags`, `entry_tags`, `card_purchase_tags`). Migração `Tags`:

- tabela `tags` (como no documento; `name varchar(30)`, `name_normalized` único parcial, `color` com
  `CHECK`, `is_settlement boolean default false`, `archived_at`);
- tabelas `entry_tags (entry_id, tag_id)` e `card_purchase_tags (card_purchase_id, tag_id)` com
  `tenant_id`, PK composta, FKs compostas para os dois lados, índice `(tenant_id, tag_id)`;
  `ITenantEntity` sem auditoria/soft delete (ver como o teste de arquitetura classifica — se exigir,
  criar a marcação `ITenantLink` e documentar em `docs/arquitetura.md` §3.2);
- em `entries` e `card_purchases`: `settlement_tag_id uuid null` (FK composta → tags; índice
  `(tenant_id, settlement_tag_id)`) e `split_group_id uuid null` (índice).

Restrição "ligação só com tag não de acerto" é validada na aplicação (o banco não consegue checar
coluna de outra tabela sem trigger).

Lançamento soft-deleted mantém suas ligações (restaurar volta com as tags). Excluir uma tag
(só sem uso) remove nada em cascata.

## Domínio

`Tags/Tag` (`Create`, `Rename`, `ChangeColor`, `MarkSettlement`, `UnmarkSettlement`, `Archive`,
`Unarchive`), `Settlements/SettlementCalculator` (research R3), `Splits/SplitPlanner` (partes iguais ou
valores; valida soma; research R5).
