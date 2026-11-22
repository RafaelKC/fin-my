# Fin-My — visão geral das specs

App para substituir a planilha **Planejamento Financeiro 2026** (arquivos `.xlsx`/`.ods` na raiz;
as duas versões têm o mesmo conteúdo). Lançamento rápido pelo celular + visão completa no computador.

- Princípios, glossário e **regras transversais (RT-01 a RT-12)**: `.specify/memory/constitution.md`
  (leia primeiro — as specs dependem dele).
- Cada pasta `NNN-*` é uma feature do spec-kit, com `spec.md` e `checklists/requirements.md`.
- Parte técnica transversal: [`docs/arquitetura.md`](../docs/arquitetura.md) (stack, tenant, auditoria, API,
  auth, jobs, front) e [`docs/modelo-de-dados.md`](../docs/modelo-de-dados.md) (diagramas ER e colunas).
  Cada `/speckit-plan` gera na pasta da feature: `plan.md`, `research.md`, `data-model.md` (só as
  tabelas da feature, copiadas do modelo global), `contracts/` (OpenAPI) e `quickstart.md`.

## Ordem de implementação

| # | Feature | Depende de | Entrega | Card |
|---|---|---|---|---|
| 001 | [Contas e Espaços](001-espacos-e-acesso/spec.md) | — | Cadastro, login longo no celular, isolamento entre Espaços (tenant) | #1 |
| 002 | [Carteiras e lançamentos](002-carteiras-e-lancamentos/spec.md) | 001 | Carteiras, Entradas/Saídas, visão do mês Previsto x Realizado, transferências, pendências | #2 |
| 003 | [Cartões e faturas](003-cartoes-e-faturas/spec.md) | 002 | Cartões, compras, parcelamento, faturas, pagamento de fatura, limite | #3 |
| 004 | [Tags e acertos](004-tags-e-acertos/spec.md) | 003 | Tags (no lugar de "Pessoa"), quanto cada um deve, gerar cobrança, dividir compra | #4 |
| 005 | [Recorrências](005-recorrencias/spec.md) | 004 | Contas fixas, salário, assinaturas geradas automaticamente | #6 |
| 006 | [Painel e primeiros passos](006-painel-e-primeiros-passos/spec.md) | 005 | Painel do ano (aba Home), resumo de agora, guia inicial, exportar CSV | #7 |
| 007 | [Planejamento do ano](007-planejamento-anual/spec.md) | 006 | Grade do ano editável como a planilha, estimativa de gasto por fatura, copiar o planejamento do ano anterior | #8 |
| 008 | [Assistente no Claude](008-assistente-claude/spec.md) | 004 | Conector MCP + skill: lançar, consultar e acertar conversando com o Claude | #5 |

Com 001 + 002 + 003 o app já substitui a planilha no dia a dia (MVP). 004 a 008 completam (a 008 pode vir logo após a 004).

Cards: épicos no [GitHub Project Fin-My](https://github.com/users/RafaelKC/projects/4) (Status: Spec → Plano → Tarefas → Em andamento → Pronto). Após `/speckit-tasks`, cada tarefa vira um card com label `tarefa`, como sub-issue do épico.

Fluxo por feature: `/speckit-clarify` (opcional) → `/speckit-plan` → `/speckit-tasks` →
`/speckit-analyze` → `/speckit-implement`. O `.specify/feature.json` aponta para a feature atual
(começa em 001); atualize-o ao passar para a próxima.

## Como a planilha vira o app

| Planilha | App |
|---|---|
| Uma aba por mês (JAN…DEZ) | Visão do mês, navegável para qualquer mês/ano (002) |
| Bloco Entradas / Saídas com caixinha | Entradas e Saídas com Situação Prevista ↔ Recebida/Paga, um toque (002) |
| Resultados "Atual" x "Projetado" | Realizado x Previsto (RT-06, 002 FR-015) |
| "Saldo Mês Anterior" puxando o mês anterior | Saldo inicial do mês calculado automaticamente (002 FR-015) |
| Uma conta bancária implícita | Carteiras com saldo (002) |
| Valor digitado como conta (`=29+73,2`) | Campo de valor com calculadora (RT-05) |
| Bloco Cartão de Crédito + coluna Cartão | Compras no Cartão, distribuídas em Faturas pelo dia de fechamento (003) |
| "Shoppe Casa 5/8" copiado à mão todo mês | Parcelamento automático; "parcelamento em andamento" para migrar (003) |
| Linha de Saída "Cartão Azul" = total do cartão | Fatura entra sozinha como saída no mês do vencimento (RT-07, 003) |
| Valores negativos (estorno) | Compra de natureza Crédito (003) |
| Coluna Pessoa (Lia, Duda, Carla, Bruna) | Tags; pessoas que devolvem dinheiro = Tags de acerto (004) |
| Bloco Pessoas + Entrada "Bruna" = total do mês anterior | Tela Acertos + "Gerar cobrança" (004) |
| `=239,8/2` em duas linhas | "Dividir" entre pessoas (004) |
| Capital Social, Tim, Seguro, Prime Video copiados todo mês | Recorrências (005) |
| Salário de 25/02 lançado na aba de março | Mês de referência (RT-04) / recorrência "mês seguinte à data" (005) |
| Aba Home (12 meses, resultado negativo em vermelho) | Painel do ano (006) |
| Abas futuras preenchidas no começo do ano | Grade de planejamento do ano + estimativa de fatura + recorrências até dezembro do ano seguinte (005, 007) |
| Planilha de cada pessoa separada | Espaços isolados: cada pessoa tem sua conta (001); modelo já pronto para compartilhar um Espaço no futuro |

## Decisões tomadas

- **PWA em Angular, não app nativo.** Detalhes na constituição (Restrições de tecnologia). Atende
  "lançar pelo telefone" (instalável na tela inicial) e "visão na web" com um único código. React
  Native só se justificaria por offline completo, widgets ou biometria, que não estão na v1.
- **Tags no lugar de pessoas**, com o marcador "Tag de acerto" para quem devolve dinheiro, e no
  máximo uma Tag de acerto por lançamento (evita ambiguidade sobre quem deve).
- **Fatura pelo vencimento**: a fatura conta no mês em que vence, e o pagamento dela também
  (RT-04/RT-07), para Previsto e Realizado ficarem no mesmo mês.
- **Compra no dia do fechamento vai para a próxima fatura** (003 FR-008).
- **Sobra de centavos na divisão vai para a 1ª parte/parcela** (RT-02).
- **Sem categorias**: tags comuns cumprem esse papel (a planilha não tinha categorias).
- **Compartilhamento preparado, não exposto**: acesso por Participação (Usuário + Espaço + papel), registro de quem criou/alterou cada dado; na v1 cada Espaço tem só a dona (001 FR-005/FR-005a).
- **Planejar = lançar Previstas**: o planejamento do ano não é um "orçamento" à parte; cria Entradas/Saídas Previstas, recorrências e estimativas de fatura, então os números batem em todas as telas (007).
- **Cadastros pela própria usuária**: dias de fechamento/vencimento dos cartões e saldos das carteiras são informados por ela no guia de primeiros passos (006).
- **Sem importação**: começa do zero, usando o guia de primeiros passos e o parcelamento em andamento (003 US4).

## Fora de escopo da v1

Importação da planilha atual (decidido: não haverá); interface para compartilhar um Espaço (convites, papéis); login social/biometria/PIN;
uso offline; Open Finance/leitura automática de faturas; juros/rotativo automáticos; orçamentos/metas;
multimoeda; recorrências não mensais; notificações; exclusão de conta.

## Perguntas em aberto

Nenhuma. (Confirmado: compra no dia do fechamento vai para a fatura seguinte — 003 FR-008.)
