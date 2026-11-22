# Feature Specification: Planejamento do ano

**Feature Branch**: `007-planejamento-anual`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Gostamos de planejar o ano todo no começo." — na planilha, no início do ano as abas futuras já recebem as linhas previstas (salário, 13º, Natal, contas, parcelas, assinaturas) sem data definida, e a aba Home mostra o resultado projetado de cada mês.

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 002, 003, 005, 006.
> Esta feature NÃO cria um tipo novo de lançamento: tudo que é planejado vira Entrada/Saída
> **Prevista** comum (002), Recorrência (005) ou **Estimativa de fatura** (definida aqui). Assim os
> números do planejamento são os mesmos da visão do mês e do Painel (Princípio III).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Grade do ano (Priority: P1)

No computador, a pessoa abre uma grade parecida com a planilha: uma linha por item (Salário, Tim,
Natal, Fatura Azul...) e uma coluna por mês. Ela digita valores direto nas células e o app cria ou
ajusta as Entradas/Saídas Previstas daquele mês. Embaixo, os totais e o saldo previsto de cada mês
se atualizam na hora.

**Why this priority**: é como o casal planeja hoje; sem isso, planejar 12 meses exigiria abrir 12 telas.

**Independent Test**: num ano vazio, criar a linha "Natal" com R$ 1.200,00 em dezembro e "13º salário" com R$ 1.500,00 em dezembro, e ver o Resultado previsto de dezembro mudar +300,00.

**Acceptance Scenarios**:

1. **Given** o ano 2027 com lançamentos, **When** a pessoa abre o Planejamento 2027, **Then** vê o bloco Entradas e o bloco Saídas, cada um com uma linha por item (FR-002) e 12 colunas com o valor Previsto do item no mês; um bloco Faturas com uma linha por cartão (valor Previsto da fatura, 003 FR-022); e as linhas de total Entradas, Saídas, Resultado e Saldo final previstos, idênticas às do Painel (006).
2. **Given** a linha "Natal" (Saída) sem nada em dezembro, **When** a pessoa digita `1200` na célula de dezembro, **Then** é criada uma Saída Prevista "Natal" de R$ 1.200,00, Mês de referência 2027-12, data conforme FR-004, carteira conforme FR-005.
3. **Given** a célula de março da linha "Tim" corresponde a uma única Saída Prevista de R$ 44,99, **When** a pessoa digita `50,99`, **Then** essa Saída passa a R$ 50,99 (se for ocorrência de recorrência, fica marcada como "alterada", 005 FR-005).
4. **Given** a célula de março da linha "Tim" corresponde a uma única Saída Prevista, **When** a pessoa apaga o conteúdo da célula, **Then** a Saída é excluída (RT-09).
5. **Given** uma célula que corresponde a dois ou mais lançamentos, ou a algum lançamento já efetivado, **When** a pessoa tenta digitar nela, **Then** o app não altera nada e abre a lista dos lançamentos daquela célula para edição individual.
6. **Given** a célula aceita valores, **When** a pessoa digita `=1100-500` ou `1100-500`, **Then** o valor é R$ 600,00 (RT-05; o "=" inicial é ignorado, como na planilha).
7. **Given** a grade, **When** a pessoa clica em "Nova linha" e informa tipo (Entrada ou Saída), descrição, carteira e dia, **Then** a linha aparece vazia, pronta para receber valores.
8. **Given** uma linha, **When** a pessoa usa "Preencher meses" informando um valor e os meses (ex.: R$ 300,00 de janeiro a dezembro), **Then** é criada uma Prevista em cada mês escolhido que ainda esteja vazio naquela linha; meses que já têm valor não são alterados e o app informa quantos foram pulados.
9. **Given** um item que se repete todo mês, **When** a pessoa usa "Transformar em recorrência" na linha, **Then** abre o cadastro de recorrência (005) preenchido com descrição, tipo, carteira, dia e o valor mais frequente da linha.

---

### User Story 2 - Estimativa de gasto no cartão (Priority: P1)

Ao planejar o ano, a pessoa ainda não sabe as compras futuras do cartão, mas sabe que gasta cerca de
R$ 800,00 por mês no Azul. Ela informa essa estimativa por fatura e o previsto do mês usa a
estimativa enquanto as compras reais não a ultrapassarem.

**Why this priority**: sem isso, os meses futuros parecem ter sobra de dinheiro porque as faturas só têm parcelas e assinaturas.

**Independent Test**: com o exemplo abaixo, conferir o valor previsto da fatura antes e depois do fechamento.

**Exemplo numérico (vira teste automatizado)** — cartão Azul (fecha 18, vence 25). Fatura 2027-05
(fecha 18/05/2027) com estimativa R$ 800,00.

| Hoje | Total lançado | Pago | Previsto da fatura | Por quê |
|---|---|---|---|---|
| 10/01/2027 | 205,53 (parcelas + assinatura) | 0,00 | 800,00 | antes do fechamento: maior entre Total, Pago e Estimativa |
| 10/05/2027 | 912,40 | 0,00 | 912,40 | compras já passaram da estimativa |
| 19/05/2027 | 640,00 | 0,00 | 640,00 | após o fechamento a estimativa deixa de valer |

**Acceptance Scenarios**:

1. **Given** o exemplo, **When** a pessoa define a estimativa de R$ 800,00 na fatura Azul 2027-05, **Then** em 10/01/2027 a linha da fatura no mês de maio mostra "Previsto R$ 800,00 — lançado R$ 205,53" e as Saídas previstas de maio usam 800,00.
2. **Given** o exemplo, **When** o total lançado passa da estimativa antes do fechamento, **Then** o Previsto passa a ser o Total lançado.
3. **Given** a fatura já fechou (hoje ≥ data de fechamento), **When** exibida, **Then** a estimativa é ignorada e o Previsto é o da regra sem estimativa (003 FR-022); a estimativa continua visível como referência "Estimado R$ 800,00".
4. **Given** a grade do ano, **When** a pessoa digita um valor na linha de um cartão, **Then** está definindo a estimativa daquela fatura (não cria compra). Apagar a célula remove a estimativa.
5. **Given** a linha de um cartão, **When** a pessoa usa "Preencher meses" com R$ 800,00 de janeiro a dezembro, **Then** a estimativa é definida em cada fatura do ano que ainda não tem estimativa.
6. **Given** uma estimativa, **When** o limite disponível do cartão é calculado (003 FR-026), **Then** a estimativa NÃO é considerada.

---

### User Story 3 - Começar o ano a partir do anterior (Priority: P2)

No começo do ano, a pessoa copia o planejamento do ano anterior (itens avulsos como Natal, 13º,
IPVA, material escolar e as estimativas de cartão) para o ano novo, revisa e ajusta.

**Why this priority**: acelera o ritual de planejar o ano; recorrências já cuidam do que é fixo.

**Independent Test**: copiar 2026 → 2027 escolhendo 2 linhas e conferir as Previstas criadas.

**Acceptance Scenarios**:

1. **Given** 2026 tem a Saída "Natal" R$ 1.200,00 em dezembro e a Entrada "13º salário" R$ 1.500,00 em dezembro, **When** a pessoa escolhe "Copiar do ano anterior" em 2027, **Then** vê a lista de linhas de 2026 com os valores por mês e pode marcar quais copiar.
2. **Given** a pessoa marcou "Natal" e "13º salário", **When** confirma, **Then** são criadas em 2027 Previstas com os mesmos valores, nos mesmos meses, mesmo dia (RT-03 para dias inexistentes) e mesma carteira (se arquivada, a principal); meses dessas linhas que já têm valor em 2027 são pulados e informados.
3. **Given** a lista de cópia, **When** exibida, **Then** ocorrências de recorrências não aparecem (a recorrência já gera o ano novo, 005) e lançamentos que são parte de divisão (004) aparecem separados por tag de acerto.
4. **Given** estimativas de fatura em 2026, **When** a pessoa marca "copiar estimativas de cartão", **Then** cada fatura de 2027 sem estimativa recebe a estimativa da fatura do mesmo mês de 2026.

---

### Edge Cases

- Linha cujo único lançamento muda de descrição: ele passa para a linha da nova descrição; linha que fica sem lançamentos no ano some, exceto linhas criadas com "Nova linha" e ainda vazias, que permanecem até a pessoa sair da tela.
- Lançamentos de Compras não aparecem como linhas: o cartão aparece apenas pela linha de fatura.
- Transferências não aparecem na grade (RT-08).
- Célula com valor de lançamento de outra carteira que não a da linha: aparece normalmente (a linha agrupa por descrição, não por carteira).
- No celular, a grade é exibida com rolagem horizontal dos meses e a primeira coluna fixa; editar células funciona, mas "Copiar do ano anterior" e "Preencher meses" podem ficar só no computador.

## Requirements *(mandatory)*

### Functional Requirements

**Grade**

- **FR-001**: A tela Planejamento MUST mostrar um ano escolhido com blocos Entradas, Saídas e Faturas e as linhas de total Entradas, Saídas, Resultado e Saldo final previstos, iguais ao Painel (006 FR-001, leitura Previsto).
- **FR-002**: Linha de Entradas/Saídas = conjunto das Entradas (ou Saídas) do ano cujas descrições são iguais ignorando maiúsculas/minúsculas, acentos e espaços nas pontas. O nome exibido é a descrição do lançamento mais recente. Linhas em ordem alfabética. Célula = soma (Previsto) dos lançamentos da linha com Mês de referência naquele mês, com indicação visual quando todos estão efetivados.
- **FR-003**: Célula editável somente quando corresponde a zero lançamentos ou a exatamente um lançamento Previsto; nos demais casos, abre a lista (cenário 1.5).
- **FR-004**: Data de uma Prevista criada pela grade = o dia (RT-03) do lançamento mais recente da linha; se a linha não tem lançamentos, o dia informado em "Nova linha"; se nenhum, dia 1. Mês de referência = mês da coluna.
- **FR-005**: Carteira de uma Prevista criada pela grade = a carteira do lançamento mais recente da linha (se ativa), senão a informada em "Nova linha", senão a Carteira principal. Tags: as do lançamento mais recente da linha.
- **FR-006**: Valor digitado na célula segue RT-05 (aceitando um "=" inicial); apagar a célula aplica o cenário 1.4.
- **FR-007**: "Preencher meses" e "Transformar em recorrência" MUST seguir os cenários 1.8 e 1.9.
- **FR-008**: Totais e saldos MUST se atualizar imediatamente após cada edição de célula.

**Estimativa de fatura**

- **FR-009**: Cada Fatura MAY ter uma Estimativa (RT-02, RT-05). Ela é definida pela linha do cartão na grade ou pela tela da fatura (003).
- **FR-010**: Com Estimativa E, o valor Previsto da fatura (substitui a regra da 003 FR-022 enquanto valer): se hoje < data de fechamento → maior valor entre Total, Pago, E e 0; senão → regra da 003 FR-022 (sem estimativa). O Realizado não muda.
- **FR-011**: Com filtro por Carteira (003 FR-023), a diferença entre o Previsto com estimativa e o Previsto sem estimativa é atribuída à carteira de pagamento do cartão.
- **FR-012**: Estimativas MUST NOT afetar limite disponível (003 FR-026), acertos por tag (004), Total/Restante/situação da fatura nem o saldo atual das carteiras.

**Copiar do ano anterior**

- **FR-013**: "Copiar do ano anterior" MUST seguir os cenários 3.1 a 3.4: nada é criado sem confirmação; cópias são Previstas comuns, sem vínculo com a origem.

### Key Entities

- **Estimativa de fatura**: Fatura + valor. (Nova.)
- Linhas da grade não são guardadas como entidade: são calculadas a partir dos lançamentos (exceto linhas novas ainda vazias, que existem só enquanto a tela está aberta).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Planejar um ano completo (recorrências já cadastradas + 10 itens avulsos + estimativa de 3 cartões) leva menos de 20 minutos no computador.
- **SC-002**: Os totais da grade são idênticos aos do Painel e da visão de cada mês (teste automatizado).
- **SC-003**: O exemplo numérico da US2 passa como teste automatizado.

## Assumptions

- Não se guarda um "plano original" separado do previsto atual: ao ajustar uma Prevista, o plano muda. Comparar "planejado em janeiro x realizado" fica fora da v1; a comparação disponível é Previsto x Realizado.
- Orçamento por tag/categoria fica fora da v1.
