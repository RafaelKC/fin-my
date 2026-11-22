# Feature Specification: Carteiras, Entradas, Saídas e visão do mês

**Feature Branch**: `002-carteiras-e-lancamentos`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Transformar a planilha mensal (blocos Entradas e Saídas, com caixinha de 'já aconteceu', resultados Atual x Projetado e saldo do mês anterior levado para o mês seguinte) num app fácil de lançar pelo celular, com cadastro de carteiras."

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 001. Esta feature NÃO inclui cartões (003), tags (004) nem recorrências (005); onde essas
> features se encaixam está indicado como "ponto de extensão".

## Como a planilha funciona hoje (referência)

Cada aba mensal (JAN…DEZ) tem: bloco **Entradas** (Nome, Data, Valor, caixinha), bloco **Saídas**
(Nome, Data, Valor, caixinha) e um bloco de **Resultados** com duas colunas: **Atual** (soma só das
linhas com a caixinha marcada) e **Projetado** (soma de todas). A primeira entrada de cada mês é
"Saldo Mês Anterior", que puxa o Resultado Projetado do mês anterior. Valores são frequentemente
digitados como contas (`=29+73,2`). Este app reproduz esse modelo com: caixinha → Situação;
Atual → Realizado; Projetado → Previsto; "Saldo Mês Anterior" → Saldo inicial do mês calculado
automaticamente; e acrescenta Carteiras (a planilha tinha uma só conta implícita).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cadastrar Carteiras (Priority: P1)

A pessoa cadastra onde o dinheiro dela fica (ex.: "Azul", "Verde", "VR/VA", "Dinheiro"), com o
saldo que existe hoje, e vê o saldo atual de cada uma.

**Why this priority**: todo lançamento de Entrada/Saída precisa de uma Carteira.

**Independent Test**: cadastrar duas carteiras com saldos iniciais e ver a lista com os saldos.

**Acceptance Scenarios**:

1. **Given** um Espaço sem carteiras, **When** a pessoa cadastra "Azul" com saldo inicial R$ 500,00 na data 01/03/2026, **Then** a carteira aparece com saldo atual R$ 500,00 e é marcada como Carteira principal (por ser a primeira).
2. **Given** já existe a carteira "Azul", **When** a pessoa tenta cadastrar "azul", **Then** o app avisa que já existe uma carteira com esse nome.
3. **Given** a carteira "Azul" tem lançamentos, **When** a pessoa tenta excluí-la, **Then** o app oferece apenas Arquivar (RT-11).
4. **Given** "Azul" é a Carteira principal, **When** a pessoa a arquiva, **Then** o app pede para escolher outra carteira ativa como principal antes de concluir; se não houver outra carteira ativa, o arquivamento é bloqueado com explicação.

---

### User Story 2 - Lançar Entrada ou Saída pelo celular em segundos (Priority: P1)

A pessoa abre o app, toca em "+", digita o valor (pode ser uma conta, como na planilha), a descrição
(com sugestões do que já lançou antes) e salva. Data, carteira e situação vêm preenchidas.

**Why this priority**: é o uso mais frequente e o motivo de o app existir (Princípio I).

**Independent Test**: lançar uma Saída de R$ 44,99 "Tim" e vê-la no mês com situação Paga, em menos de 15 segundos.

**Acceptance Scenarios**:

1. **Given** hoje é 10/03/2026 e a carteira principal é "Azul", **When** a pessoa toca "+", escolhe Saída, digita `44,99`, descreve "Tim" e salva, **Then** é criada uma Saída de R$ 44,99, data 10/03/2026, carteira Azul, situação Paga, Mês de referência 2026-03.
2. **Given** a pessoa digita `29+73,2` no valor, **When** salva, **Then** o valor gravado é R$ 102,20 (RT-05).
3. **Given** a pessoa escolhe a data 25/03/2026 (futura em relação a hoje, 10/03), **When** salva sem mexer na situação, **Then** a situação é Prevista.
4. **Given** a pessoa já lançou "Tim" antes na carteira "Verde", **When** digita "Ti" na descrição e escolhe a sugestão "Tim" antes de mexer no campo carteira, **Then** a carteira do formulário passa a ser "Verde" (a da última vez que "Tim" foi lançado); o valor NÃO é preenchido.
5. **Given** o formulário preenchido, **When** a pessoa toca "Salvar e lançar outro", **Then** o lançamento é salvo e o formulário reabre limpo mantendo tipo, data e carteira.
6. **Given** o valor digitado é `0` ou `-5` numa Entrada/Saída, **When** tenta salvar, **Then** o app mostra "Informe um valor maior que zero" e não salva.
7. **Given** sem conexão com a internet, **When** a pessoa toca Salvar, **Then** o app avisa que não há conexão e mantém o formulário preenchido.

---

### User Story 3 - Ver o mês: Previsto x Realizado e saldo levado ao mês seguinte (Priority: P1)

A pessoa abre a visão do mês e vê, como na planilha, as Entradas e Saídas do mês com a situação de
cada uma, e um resumo com Saldo inicial, Entradas, Saídas, Resultado e Saldo final, nas leituras
Previsto e Realizado. O saldo final de um mês é o saldo inicial do seguinte, automaticamente.

**Why this priority**: é a "aba do mês" da planilha; sem ela o app não substitui a planilha.

**Independent Test**: com o exemplo numérico abaixo, conferir todos os números do resumo de março e o saldo inicial de abril.

**Exemplo numérico (vira teste automatizado)** — carteira única "Azul", saldo inicial R$ 500,00 em 01/03/2026; lançamentos com Mês de referência 2026-03:

| Tipo | Descrição | Valor | Situação |
|---|---|---|---|
| Entrada | Salário | 1.500,00 | Recebida |
| Entrada | Duda | 18,27 | Prevista |
| Saída | Tim | 44,99 | Paga |
| Saída | Léo Casa | 770,03 | Prevista |

| Resumo de março | Previsto | Realizado |
|---|---|---|
| Saldo inicial | 500,00 | 500,00 |
| Entradas | 1.518,27 | 1.500,00 |
| Saídas | 815,02 | 44,99 |
| Resultado | 703,25 | 1.455,01 |
| Saldo final | 1.203,25 | 1.955,01 |

Abril (sem lançamentos): Saldo inicial Previsto 1.203,25 e Realizado 1.955,01. Saldo atual da
carteira Azul: R$ 1.955,01.

**Acceptance Scenarios**:

1. **Given** o exemplo acima, **When** a pessoa abre março/2026, **Then** vê exatamente os valores da tabela de resumo.
2. **Given** o exemplo acima, **When** a pessoa marca "Duda" como Recebida com um toque na lista, **Then** Entradas Realizado passa para 1.518,27, Resultado Realizado para 1.473,28 e Saldo final Realizado para 1.973,28, sem recarregar a tela manualmente.
3. **Given** a pessoa está em março, **When** desliza para o lado ou toca na seta, **Then** vai para abril (ou fevereiro), mantendo os filtros.
4. **Given** um mês com Resultado Previsto negativo, **When** exibido, **Then** o valor aparece destacado em vermelho (como a formatação condicional da planilha).
5. **Given** o app é aberto, **When** carrega, **Then** a tela inicial é o mês de hoje (a 006 permite trocar a tela inicial pelo Painel nas preferências).

---

### User Story 4 - Pendências de meses anteriores (Priority: P2)

Entradas e Saídas que ficaram Previstas em meses que já passaram (ex.: "Carla atrasado") aparecem
como pendências no mês atual, para a pessoa decidir o que fazer.

**Why this priority**: na planilha isso era feito copiando a linha para o mês seguinte à mão.

**Independent Test**: criar uma Entrada Prevista em fevereiro, abrir março e resolver a pendência.

**Acceptance Scenarios**:

1. **Given** hoje é 10/03/2026 e existe Entrada Prevista "Carla atrasado" R$ 300,00 com Mês de referência 2026-02, **When** a pessoa abre março, **Then** vê o aviso "1 pendência de meses anteriores: a receber R$ 300,00".
2. **Given** a pendência acima, **When** a pessoa escolhe "Trazer para este mês", **Then** o Mês de referência passa a 2026-03 (a data não muda) e o valor deixa de contar em fevereiro e passa a contar em março.
3. **Given** a pendência acima, **When** a pessoa escolhe "Marcar como recebida", **Then** a situação vira Recebida e o Mês de referência continua 2026-02.
4. **Given** a pendência acima, **When** a pessoa a exclui, **Then** aplica-se RT-09.

---

### User Story 5 - Transferir entre Carteiras (Priority: P2)

A pessoa registra que passou dinheiro de uma carteira para outra (ex.: R$ 20,00 do Azul para a
Poupança), sem que isso conte como gasto nem como ganho.

**Why this priority**: na planilha a "Poupança" era lançada como saída, distorcendo o resultado.

**Independent Test**: transferir e verificar saldos das duas carteiras e totais do mês inalterados.

**Acceptance Scenarios**:

1. **Given** Azul com saldo atual 1.000,00 e Poupança com 0,00, **When** a pessoa transfere 20,00 de Azul para Poupança em 29/06/2026, **Then** Azul fica 980,00, Poupança 20,00, e Entradas/Saídas/Resultado de junho (visão de todas as carteiras) não mudam (RT-08).
2. **Given** a visão de junho filtrada pela carteira Azul, **When** exibida, **Then** a transferência aparece como "Transferência enviada" de 20,00 e o Saldo final dessa carteira reflete a saída.
3. **Given** origem e destino iguais, **When** tenta salvar, **Then** o app impede.

---

### User Story 6 - Editar, excluir, duplicar e buscar lançamentos (Priority: P2)

A pessoa corrige qualquer campo de um lançamento, exclui com possibilidade de desfazer, duplica um
lançamento para outra data, e busca lançamentos por texto e filtros.

**Why this priority**: na planilha editar é trivial; o app precisa ser igualmente fácil.

**Independent Test**: editar valor e data de uma Saída, excluir e desfazer, buscar por "Uber".

**Acceptance Scenarios**:

1. **Given** uma Saída com Mês de referência igual ao mês da data (02/03/2026 → 2026-03), **When** a data é alterada para 05/04/2026, **Then** o Mês de referência passa a 2026-04 (RT-04).
2. **Given** uma Saída com data 25/02/2026 e Mês de referência alterado manualmente para 2026-03, **When** a data é alterada para 26/02/2026, **Then** o Mês de referência continua 2026-03 (RT-04).
3. **Given** uma Saída, **When** a pessoa exclui e toca "Desfazer" em até 8 segundos, **Then** o lançamento volta idêntico (RT-09).
4. **Given** uma Saída "Tim" de 10/03/2026, **When** a pessoa escolhe "Duplicar" e informa a data 10/04/2026, **Then** é criada uma nova Saída igual, com data 10/04/2026, Mês de referência 2026-04 e situação definida pela regra de situação padrão (FR-012).
5. **Given** vários meses de lançamentos, **When** a pessoa busca "uber", **Then** vê todos os lançamentos cuja descrição contém "uber" (sem diferenciar maiúsculas/minúsculas e acentos), de qualquer mês, com o total da lista.

---

### Edge Cases

- Primeiro lançamento sem nenhuma carteira cadastrada: o formulário oferece criar a carteira ali mesmo (nome e saldo inicial) sem perder o que foi digitado.
- Lançamento com data anterior à data do saldo inicial da carteira: é permitido, mas o app avisa "Esta data é anterior ao saldo inicial da carteira. O saldo inicial já deveria incluir este valor." e o lançamento conta normalmente nos cálculos.
- Entrada/Saída marcada como Recebida/Paga com data futura: permitido (ex.: pagamento agendado); conta no Realizado.
- Entrada/Saída Prevista com data passada: exibida com o selo "Atrasada".
- Mês sem nenhum lançamento: a visão mostra o resumo (com saldo inicial herdado) e uma mensagem convidando a lançar.
- Carteira com saldo negativo: permitido e exibido em vermelho.
- Mudança do Mês de referência para um mês muito distante: permitido dentro do intervalo de datas da RT-03.

## Requirements *(mandatory)*

### Functional Requirements

**Carteiras**

- **FR-001**: O sistema MUST permitir cadastrar Carteira com: nome (1 a 40 caracteres, único no Espaço sem diferenciar maiúsculas/minúsculas e acentos), tipo (Conta corrente, Conta digital, Poupança, Dinheiro, Vale-refeição/alimentação, Outro — apenas informativo, sem efeito nas regras), cor, saldo inicial (qualquer valor, inclusive zero ou negativo; exceção à RT-02) e data do saldo inicial (padrão: hoje).
- **FR-002**: Exatamente uma Carteira ativa MUST ser a Carteira principal sempre que existir pelo menos uma Carteira ativa. A primeira cadastrada torna-se principal automaticamente; a pessoa pode trocar a qualquer momento.
- **FR-003**: Saldo atual da Carteira = saldo inicial + Entradas Recebidas − Saídas Pagas − Pagamentos de fatura feitos com ela + Transferências recebidas − Transferências enviadas, considerando todos os lançamentos dessa carteira, independentemente de data ou mês.
- **FR-004**: Arquivamento e exclusão de Carteiras MUST seguir RT-11 e o cenário 1.4.

**Entradas e Saídas**

- **FR-005**: Entrada e Saída MUST ter: descrição (RT-05a), valor (RT-02, RT-05), data (RT-03), Carteira (ativa), Situação, Mês de referência (RT-04) e observação opcional (até 500 caracteres).
- **FR-006**: Situação: Entrada = Prevista ou Recebida; Saída = Prevista ou Paga. A situação MUST poder ser alternada com um único toque na lista do mês (equivalente à caixinha da planilha).
- **FR-007**: Pagamentos de fatura e Transferências MUST NOT ser criados como Entrada/Saída; têm fluxos próprios (Transferência nesta feature; Pagamento de fatura na 003).

**Formulário de lançamento rápido**

- **FR-008**: Um botão "+" MUST estar visível em todas as telas principais e abrir o formulário com o teclado numérico já ativo no campo valor.
- **FR-009**: O formulário MUST ter seletor de tipo no topo. Nesta feature: Saída, Entrada, Transferência. (Ponto de extensão: a 003 acrescenta "Cartão".) O tipo inicial é o último tipo salvo naquele aparelho; no primeiro uso, Saída.
- **FR-010**: Ordem e padrões dos campos: valor; descrição com sugestões; data = hoje; Carteira = a última usada no mesmo tipo naquele aparelho, ou a principal se ela não estiver mais ativa ou nunca houve uso; Situação conforme FR-012. Mês de referência e observação ficam em "Mais opções" (recolhido).
- **FR-011**: Sugestões de descrição MUST listar descrições usadas no Espaço nos últimos 12 meses que contenham o texto digitado (sem diferenciar maiúsculas/minúsculas e acentos), ordenadas por quantidade de usos, no máximo 6. Ao escolher uma sugestão, a Carteira (e, a partir da 004, as tags) do lançamento mais recente com essa descrição exata e do mesmo tipo MUST ser aplicada aos campos que a pessoa ainda não alterou manualmente naquele formulário. O valor nunca é preenchido pela sugestão.
- **FR-012**: Situação padrão: se data ≤ hoje → Recebida/Paga; se data > hoje → Prevista. Se a pessoa alterar a situação manualmente, mudanças posteriores de data no mesmo formulário não a alteram mais.
- **FR-013**: "Salvar" fecha o formulário; "Salvar e lançar outro" salva e reabre limpo mantendo tipo, data e Carteira.

**Visão do mês**

- **FR-014**: A visão do mês MUST mostrar: resumo (FR-015), lista de Entradas, lista de Saídas (ponto de extensão: a 003 inclui as Faturas nesta lista), lista de Transferências (recolhida por padrão) e o aviso de pendências (FR-019). Listas seguem RT-12.
- **FR-015**: Resumo do mês M, em Previsto e Realizado (RT-06), para o conjunto de Carteiras selecionado (padrão: todas as ativas e arquivadas):
  - **Entradas(M)** = soma das Entradas com Mês de referência M.
  - **Saídas(M)** = soma das Saídas com Mês de referência M + (ponto de extensão 003) valor das Faturas do mês M conforme RT-07.
  - **Resultado(M)** = Entradas(M) − Saídas(M).
  - **Saldo final(M)** = soma dos saldos iniciais das Carteiras selecionadas cuja data do saldo inicial está em M ou antes + soma de Resultado de todos os meses até M, inclusive + Transferências recebidas pelas Carteiras selecionadas até M − Transferências enviadas pelas Carteiras selecionadas até M (quando todas as Carteiras estão selecionadas, as transferências se anulam, RT-08). Previsto e Realizado usam a mesma fórmula com os respectivos Resultados (transferências são sempre efetivadas).
  - **Saldo inicial(M)** = Saldo final(M − 1). Se uma Carteira tem data de saldo inicial dentro de M, o saldo inicial dela aparece numa linha própria "Saldo inicial de carteiras" no resumo de M e entra no Saldo final(M).
- **FR-016**: O resumo MUST se atualizar imediatamente após qualquer criação, edição, exclusão ou troca de situação.
- **FR-017**: Valores de Resultado e Saldo final negativos MUST ser destacados em vermelho.
- **FR-018**: A visão MUST permitir filtrar por Carteira (uma, várias ou todas) e por Situação; a navegação entre meses mantém os filtros.

**Pendências**

- **FR-019**: Pendência = Entrada ou Saída Prevista com Mês de referência anterior ao mês de hoje. O mês de hoje MUST exibir quantas são e os totais a receber e a pagar, com acesso à lista.
- **FR-020**: Para cada pendência (e para várias selecionadas de uma vez) a pessoa MUST poder: marcar como Recebida/Paga, trazer para o mês atual (muda só o Mês de referência para o mês de hoje) ou excluir.

**Transferências**

- **FR-021**: Transferência MUST ter: Carteira de origem, Carteira de destino (diferente da origem), valor (RT-02, RT-05), data, descrição (padrão "Transferência"). É sempre efetivada. Segue RT-08.

**Edição, exclusão, duplicação, busca**

- **FR-022**: Todos os campos de qualquer lançamento desta feature MUST ser editáveis a qualquer momento, sem restrição por mês.
- **FR-023**: Exclusão segue RT-09.
- **FR-024**: "Duplicar" MUST criar um novo lançamento com os mesmos dados, pedindo apenas a nova data; Mês de referência e Situação são recalculados pelas regras de criação.
- **FR-025**: A busca MUST encontrar lançamentos por trecho da descrição (sem diferenciar maiúsculas/minúsculas e acentos), em qualquer mês, com filtros por tipo, Carteira, Situação e período, e mostrar o total dos resultados (Entradas somam positivo, Saídas negativo).
- **FR-026**: No computador, a lista do mês MUST ser exibida em formato de tabela com colunas Descrição, Data, Valor, Carteira, Situação, permitindo ordenar por coluna e editar a situação com um clique.

### Key Entities

- **Carteira**: nome, tipo, cor, saldo inicial, data do saldo inicial, principal (sim/não), arquivada (sim/não).
- **Entrada / Saída**: descrição, valor, data, Mês de referência, Situação, Carteira, observação. (Tags: 004. Origem em Recorrência: 005.)
- **Transferência**: Carteira de origem, Carteira de destino, valor, data, descrição.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Lançar uma Saída no celular, com a descrição vinda de sugestão, leva no máximo 15 segundos e 4 toques após abrir o app.
- **SC-002**: O exemplo numérico da User Story 3 produz exatamente os valores da tabela em celular e computador.
- **SC-003**: Para qualquer conjunto de dados, a soma dos saldos atuais das Carteiras é igual ao Saldo final Realizado (visão de todas as carteiras) do último mês que contenha um lançamento efetivado ou uma data de saldo inicial (teste automatizado de consistência).
- **SC-004**: Uma pessoa que usa a planilha consegue, sem ajuda, reproduzir um mês da planilha no app em até 30 minutos.

## Assumptions

- Não existem categorias além das Tags (004); a planilha também não tinha.
- A ordem de exibição e os rótulos podem ser refinados no design, desde que as regras sejam mantidas.
- Lançamentos com valores em outras moedas estão fora de escopo.
- Importação da planilha existente está fora de escopo (ver `specs/README.md`).
