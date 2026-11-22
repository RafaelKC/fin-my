# Constituição do Fin-My

App de controle financeiro pessoal que substitui a planilha "Planejamento Financeiro 2026".
Público principal: uma pessoa não técnica que hoje lança tudo numa planilha mensal e quer lançar pelo
celular com a mesma rapidez, e ver o panorama completo no computador.

Este documento é lido por todos os comandos do spec-kit (`/speckit-plan`, `/speckit-tasks`,
`/speckit-implement`...). Ele contém os **princípios**, o **glossário** e as **regras transversais (RT)**
que valem para TODAS as features. As specs em `specs/NNN-*/spec.md` referenciam as RT pelo código
(ex.: "conforme RT-02") e NUNCA as redefinem. Se uma spec parecer contradizer uma RT, a RT prevalece e
a spec deve ser corrigida.

## Core Principles

### I. Tão fácil quanto a planilha (NÃO NEGOCIÁVEL)

- O fluxo mais frequente — registrar um gasto no cartão pelo celular — deve ser concluído em até
  15 segundos e no máximo 4 toques após abrir o app (abrir formulário, digitar valor, digitar/escolher
  descrição, salvar), usando os padrões inteligentes definidos nas specs.
- Toda tela deve funcionar em celular (largura a partir de 360 px) e em computador. O celular é
  otimizado para lançar e consultar o mês; o computador, para revisar, editar em massa e ver o ano.
- Nada de jargão contábil na interface. Os termos visíveis são os do glossário abaixo.
- Toda ação destrutiva oferece "Desfazer" (RT-09) em vez de pedir confirmação, exceto exclusões em
  massa ou de cadastros com lançamentos vinculados, que pedem confirmação explícita.

### II. Isolamento total entre Espaços (NÃO NEGOCIÁVEL)

- Todo dado financeiro pertence a exatamente um Espaço. Nenhuma consulta, relatório, busca,
  sugestão de autocompletar, exportação ou mensagem de erro pode revelar dados de outro Espaço.
- O isolamento deve ser coberto por testes automatizados para cada tipo de dado.

### III. Exatidão monetária (NÃO NEGOCIÁVEL)

- Valores monetários são armazenados e calculados em centavos inteiros (nunca ponto flutuante).
- Toda regra de cálculo (saldos, faturas, parcelas, divisões, totais por tag) é determinística e
  coberta por testes automatizados com os exemplos numéricos das specs antes de ser implementada
  (teste primeiro).
- O mesmo número deve aparecer igual em todas as telas (celular, computador, painel anual, mês).
  Totais nunca são "somados na tela": vêm de uma única regra de cálculo.

### IV. Regras de negócio explícitas e únicas

- Cada regra de negócio é definida em um único lugar (uma RT aqui ou um FR numa spec) e referenciada
  nos demais. Ambiguidade encontrada durante implementação deve ser registrada como pergunta
  (`/speckit-clarify`) — nunca resolvida por suposição silenciosa.

### V. Simplicidade (YAGNI)

- Implementar só o que as specs pedem. Itens listados em "Fora de escopo" não devem ser
  implementados nem "preparados" além do mínimo necessário.

## Glossário (linguagem do domínio)

Os nomes abaixo são os usados na interface, nas specs e, sempre que possível, no código.

| Termo | Definição |
|---|---|
| **Usuário** | Pessoa com login (e-mail e senha). |
| **Espaço** | Conjunto isolado de dados financeiros (o "tenant"). O acesso é dado por **Participação** (Usuário + Espaço + papel). Na v1, cada Usuário participa de exatamente um Espaço, criado no cadastro, como Dono, e cada Espaço tem um único participante. O modelo já suporta vários participantes e vários Espaços por Usuário no futuro (001 FR-005a). |
| **Carteira** | Lugar onde o dinheiro fica: conta corrente, conta digital, poupança, dinheiro em espécie, vale-alimentação etc. Tem saldo. |
| **Cartão** | Cartão de crédito. Não tem saldo; acumula Compras em Faturas. |
| **Entrada** | Dinheiro que entra numa Carteira (salário, reembolso, Pix recebido). |
| **Saída** | Dinheiro que sai de uma Carteira, exceto pagamento de fatura e transferência (conta de celular, Pix enviado, débito). |
| **Compra** | Lançamento feito num Cartão. Pode ser parcelada. Tem natureza **Débito** (compra, juros, multa, tarifa) ou **Crédito** (estorno, devolução, desconto). |
| **Parcela** | Cada uma das partes de uma Compra parcelada; cada parcela cai numa Fatura diferente. |
| **Fatura** | Conjunto das Compras de um Cartão num ciclo. Identificada por Cartão + mês de vencimento. |
| **Pagamento de fatura** | Saída de dinheiro de uma Carteira para quitar (total ou parcialmente) uma Fatura. |
| **Transferência** | Movimentação de dinheiro entre duas Carteiras do mesmo Espaço. Não é Entrada nem Saída. |
| **Lançamento** | Termo genérico para Entrada, Saída, Compra, Pagamento de fatura ou Transferência. |
| **Situação** | Para Entrada: **Prevista** ou **Recebida**. Para Saída: **Prevista** ou **Paga**. "Efetivado" = Recebida ou Paga. |
| **Mês de referência** | Mês (AAAA-MM) em que uma Entrada ou Saída é contabilizada na visão mensal (RT-04). |
| **Tag** | Etiqueta livre aplicada a lançamentos (substitui a coluna "Pessoa" da planilha e também serve como categoria). |
| **Tag de acerto** | Tag que representa alguém que deve devolver o dinheiro gasto em seu nome (ex.: "Bruna", "Carla"). Permite calcular quanto cobrar. |
| **Previsto / Realizado** | Duas leituras de todo total: Previsto considera todos os lançamentos; Realizado só os efetivados (RT-06). Equivalem às colunas "Projetado" e "Atual" da planilha. |
| **Recorrência** | Modelo que gera automaticamente lançamentos mensais (salário, conta de celular, assinaturas). |

## Regras transversais (RT)

**RT-01 — Isolamento.** Todo Lançamento, Carteira, Cartão, Fatura, Tag e Recorrência pertence a um
Espaço. Um lançamento só pode referenciar Carteiras, Cartões, Faturas e Tags do mesmo Espaço.
Identificadores de outro Espaço são tratados como inexistentes ("não encontrado"), nunca como
"sem permissão".

**RT-02 — Dinheiro.**
- Moeda única: Real (BRL). Precisão: centavos.
- Todo valor digitado é positivo e maior que zero; o sentido vem do tipo (Entrada/Saída) ou da
  natureza (Débito/Crédito). Valor zero ou negativo é rejeitado com mensagem clara (exceção: se a
  pessoa digitar um valor negativo numa Compra, o app troca a natureza para Crédito e usa o valor
  absoluto, avisando "Lançado como estorno/crédito").
- Arredondamento: sempre "meio para cima" (0,005 → 0,01) para 2 casas decimais, aplicado uma única vez
  ao fim de cada cálculo.
- Divisão de um valor V em N partes (parcelas ou divisão entre tags): cada parte recebe
  `piso(V / N)` em centavos; a diferença (V − N × parte) é somada à **primeira** parte.
  Exemplo: R$ 100,00 em 3 → 33,34 + 33,33 + 33,33. Exemplo: R$ 239,80 em 2 → 119,90 + 119,90.
- Valor máximo por lançamento: R$ 9.999.999,99.

**RT-03 — Datas e hoje.**
- Fuso horário de referência: America/Sao_Paulo. "Hoje" é a data atual nesse fuso.
- Datas são somente dia/mês/ano (sem hora).
- Quando uma regra pede o "dia D" de um mês que não tem esse dia (ex.: dia 31 em fevereiro), usa-se o
  último dia do mês.
- Lançamentos podem ter qualquer data entre 01/01/2000 e 31/12/2100.

**RT-04 — Mês de referência.**
- Toda Entrada e Saída tem Mês de referência. Ao criar, ele é igual ao mês da data. A pessoa pode
  alterá-lo livremente (ex.: salário recebido em 25/02 que paga as contas de março → Mês de
  referência 2026-03). Se depois a data for alterada, o Mês de referência NÃO muda sozinho, exceto
  se ele ainda for igual ao mês da data antiga (ou seja, nunca foi alterado manualmente).
- Compras não têm Mês de referência: elas pertencem a uma Fatura, e a Fatura entra na visão mensal
  pelo mês do seu vencimento (RT-07).
- Pagamentos de fatura têm como Mês de referência o mês de vencimento da Fatura paga (mesmo que o
  pagamento seja feito antes ou depois desse mês). Isso mantém o pagamento no mesmo mês em que a
  Fatura aparece como saída prevista (RT-07).
- Transferências usam o mês da própria data.

**RT-05 — Campo de valor com calculadora.** Todo campo de valor aceita um número ou uma expressão
aritmética simples, como na planilha (`29+73,2`, `239,8/2`, `(37,02+37,87)/3`).
- Operadores aceitos: `+ - * / ( )`; também `x` e `÷` como multiplicação e divisão.
- Separador decimal: vírgula. Ponto é aceito assim: ponto seguido de exatamente 3 dígitos é separador
  de milhar e é ignorado (`1.234,56` = 1234,56); ponto seguido de 1 ou 2 dígitos é decimal (`12.5` =
  12,50). Qualquer outro uso é inválido.
- O resultado é arredondado conforme RT-02 e precisa respeitar RT-02 (maior que zero, salvo a exceção
  da Compra). Expressão inválida ou divisão por zero mostra erro e não salva.
- Só o resultado é guardado. A expressão original não é guardada.

**RT-05a — Descrição.** Texto obrigatório de 1 a 80 caracteres após remover espaços nas pontas.

**RT-06 — Previsto e Realizado.**
- **Previsto** soma todos os lançamentos (efetivados ou não).
- **Realizado** soma apenas Entradas Recebidas, Saídas Pagas, Pagamentos de fatura (que são sempre
  efetivados) e Transferências (sempre efetivadas).
- Compras não têm situação; elas entram nos totais apenas por meio da Fatura (RT-07).

**RT-07 — Fatura na visão mensal.** O valor de uma Fatura entra como saída prevista no Mês de
vencimento dela. O que entra como saída realizada são os Pagamentos de fatura, no Mês de referência
deles (= mês de vencimento da Fatura, RT-04). Compras individuais nunca entram diretamente em totais de Entradas/Saídas/Resultado do mês.

**RT-08 — Transferências são neutras.** Transferências não aparecem nos totais de Entradas, Saídas ou
Resultado do Espaço; apenas alteram o saldo das duas Carteiras envolvidas. Quando a visão é filtrada
por uma única Carteira, aparecem como "Transferência recebida" ou "Transferência enviada" e entram no
saldo dessa Carteira.

**RT-09 — Exclusão com desfazer.** Ao excluir um lançamento (ou um grupo de parcelas), o app mostra
"Excluído — Desfazer" por 8 segundos. Desfazer restaura exatamente o estado anterior. Exclusões de
Carteiras, Cartões e Tags seguem regras próprias definidas nas specs.

**RT-10 — Formatação.** Interface em português do Brasil. Valores como `R$ 1.234,56`; datas como
`25/02/2026`; meses como `Fevereiro de 2026` ou `FEV/26` quando faltar espaço.

**RT-11 — Lixeira de cadastros.** Carteiras, Cartões e Tags com lançamentos vinculados não podem ser
excluídos, apenas **arquivados**. Arquivado = some das listas de escolha para novos lançamentos e das
telas de cadastro padrão (visível no filtro "Arquivados"), mas continua aparecendo nos lançamentos e
relatórios antigos. Pode ser desarquivado. Itens sem nenhum lançamento vinculado podem ser excluídos
definitivamente.

**RT-12 — Ordenação padrão de listas de lançamentos.** Data crescente; empate: ordem de criação.

## Restrições de tecnologia e produto

- Backend: .NET 10 (LTS). Banco: PostgreSQL. Front-end: Angular. Detalhes normativos em
  `docs/arquitetura.md` e `docs/modelo-de-dados.md`, que todo `/speckit-plan` MUST seguir.
- Entrega única como **PWA** (aplicação web responsiva instalável na tela inicial do celular). Não
  haverá app nativo na v1. Justificativa: um único código para celular e computador, sem loja de
  aplicativos, atualização imediata, e as funções nativas que justificariam React Native (widgets,
  uso offline completo, biometria) não estão no escopo.
- A v1 exige conexão com a internet para salvar. Sem conexão, o app avisa e mantém o formulário
  preenchido (não perde o que foi digitado) até que seja possível salvar.
- Todos os cálculos de negócio (RT-02 a RT-08 e regras das specs) são feitos no servidor; a interface
  apenas exibe. A interface pode pré-visualizar resultados (ex.: valor da parcela), mas o valor
  salvo é o calculado pelo servidor.

## Princípios técnicos (NÃO NEGOCIÁVEIS)

### T1. Espaço e auditoria pela infraestrutura, nunca caso a caso

- O Espaço (tenant) de cada registro é atribuído automaticamente na gravação, e toda leitura é
  filtrada automaticamente pelo Espaço do contexto. Sem Espaço no contexto, leituras não retornam nada
  e gravações falham (fail-closed). Nenhum caso de uso atribui `TenantId` nem filtra por Espaço
  manualmente.
- Quem criou/alterou/excluiu e quando são preenchidos automaticamente na gravação. Nenhum caso de uso
  preenche esses campos.
- Exclusão é lógica (soft delete) e também automática.
- Chaves estrangeiras entre dados de Espaço incluem o Espaço, para o banco recusar referência cruzada.
- Mecanismo: `docs/arquitetura.md` §3–§4.

### T2. Regras de negócio puras e testadas com os exemplos das specs

- Cálculos ficam no domínio, sem acesso a banco, relógio do sistema ou rede; o "hoje" vem de um
  relógio injetável.
- Testes de dados rodam em PostgreSQL real; nunca em banco simulado.
- Vetores de teste compartilhados garantem que a pré-visualização da interface e o servidor calculam
  igual.

### T3. Contrato de API explícito

- Cada feature publica seu contrato (OpenAPI) em `specs/NNN-*/contracts/` antes de implementar.
  Dinheiro trafega em centavos inteiros; erros em ProblemDetails com código estável e mensagem pt-BR.

### T4. Pronto para compartilhar, sem implementar compartilhamento

- Autorização sempre por "participa do Espaço", nunca por "é o dono do registro"
  (001 FR-005/FR-005a).

## Fluxo de desenvolvimento

- Ordem das features: 001 → 002 → 003 → 004 → (008) → 005 → 006 → 007 (ver `specs/README.md`).
- Para cada feature: `/speckit-clarify` (se houver dúvida) → `/speckit-plan` → `/speckit-tasks` →
  `/speckit-analyze` → `/speckit-implement`.
- Os exemplos numéricos das specs viram testes automatizados.
- Uma feature só é considerada pronta quando todos os cenários de aceitação passam em celular e
  computador.

## Governance

- Esta constituição prevalece sobre specs, planos e tarefas.
- Alterações exigem: atualizar este arquivo, incrementar a versão, e revisar as specs afetadas para
  remover contradições.
- Versionamento: MAJOR = princípio removido/redefinido ou RT alterada de forma incompatível;
  MINOR = princípio/RT nova; PATCH = redação.

**Version**: 1.2.0 | **Ratified**: 2026-11-22 | **Last Amended**: 2026-11-22
