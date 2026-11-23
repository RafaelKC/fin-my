# Contrato de interface — 004

| Rota | Tela | Requisitos |
|---|---|---|
| `/acertos/:mes` (`/acertos` → mês de hoje) | `‹ Março de 2026 ›`; um cartão por Tag de acerto com Gasto, Cobrado, Recebido, A cobrar, A receber, Saldo acumulado (negativo → "Crédito da <Tag>: R$ X"); botão "Gerar cobrança" quando A cobrar > 0; tocar num número abre a lista de lançamentos que o compõem | US2, US3; FR-009, FR-010 |
| `/tags` | Lista de tags (ativas; filtro Arquivadas) com cor, "de acerto", usos; editar (nome, cor, de acerto), arquivar/desarquivar, excluir (só sem uso), juntar em outra (diálogo de escolha + confirmação); conflitos de acerto mostram a lista de lançamentos | US5 |

## Casca

Barra inferior no celular: **Mês · Cartões · + · Acertos · Mais** (arquitetura §9). "Mais": Carteiras,
Busca, Tags, Perfil. Menu lateral: Mês, Cartões, Acertos, Carteiras, Busca, Tags, Perfil.

## Campo Tags (formulário rápido, Entrada/Saída/Cartão)

- Componente `TagPicker`: 8 tags mais usadas como chips de um toque (ordem `uses90d`), campo de busca
  que filtra as demais; texto sem correspondência mostra "Criar tag "X"" (cria não de acerto); texto
  igual (normalizado) a uma existente usa a existente.
- Tags de acerto aparecem com um ícone de pessoa; escolher uma segunda tag de acerto mostra "Só uma
  tag de acerto por lançamento. Usar Dividir?" com atalho para Dividir.
- Ao escolher sugestão de descrição, aplica as tags da sugestão se o campo não foi alterado.
- Transferência não tem campo Tags.

## Dividir (Saída e Cartão)

- Botão "Dividir" no formulário: lista de partes (2–10), cada uma "Eu (sem tag de acerto)" ou uma tag
  de acerto (sem repetir), modo "Partes iguais" (mostra os valores calculados pela porta TS do
  `MoneySplitter`) ou "Valores informados" (campo de valor por parte, mostra "Faltam R$ X" / "Sobram
  R$ X" e bloqueia o salvar enquanto ≠ 0).
- Partes salvas mostram o selo "Dividida em N"; tocar mostra as outras partes.

## Filtros por tag

- Visão do mês, fatura e busca: filtro "Tags" (várias); com filtro ativo, o mês mostra também as
  compras das faturas do mês com essas tags e os totais do filtro; o resumo Previsto/Realizado continua
  o do mês inteiro.
- Fatura: bloco "Por pessoa" com subtotais por Tag de acerto e "Sem tag de acerto".
