# REQUISITO OBRIGATÓRIO — REA AUTONOMOUS TECHNICAL OPERATOR

## Interface para usuário leigo, preparação automática e captura guiada

> Texto integral enviado pelo dono em 15/11/2026. Vale para todas as fases a partir da 2.

### 1. PRINCÍPIO FUNDAMENTAL

O proprietário do REA não precisa possuir conhecimentos técnicos sobre engenharia reversa, análise de protocolos, captura de pacotes, redes, programação, Python, filtros de tráfego ou ferramentas de diagnóstico.

O REA deverá assumir a responsabilidade por toda a preparação, configuração, execução técnica, monitoramento, organização e análise das capturas.

O usuário deverá atuar como operador da aplicação e jogador, seguindo instruções simples apresentadas pelo próprio REA.

**Não transferir tarefas técnicas para o usuário quando elas puderem ser automatizadas pelo sistema.**

Não pedir ao usuário para:

- Abrir terminal.
- Executar comandos PowerShell.
- Configurar manualmente o Wireshark.
- Escrever filtros de pacotes.
- Configurar adaptadores de rede.
- Instalar bibliotecas manualmente.
- Editar arquivos de configuração.
- Localizar arquivos técnicos.
- Analisar logs.
- Interpretar pacotes.
- Configurar Python.
- Corrigir dependências manualmente.

Quando uma operação técnica puder ser realizada pelo REA, deverá ser realizada automaticamente.

### 2. AUTONOMIA TÉCNICA

Criar um serviço central denominado `REA Autonomous Operator`.

Responsabilidades:

- Detectar ferramentas instaladas.
- Verificar versões e compatibilidade.
- Detectar dependências ausentes.
- Preparar diretórios de trabalho.
- Verificar permissões.
- Identificar processos e janelas.
- Detectar interfaces de rede disponíveis.
- Selecionar configurações recomendadas.
- Verificar espaço em disco.
- Preparar captura visual.
- Preparar captura de rede.
- Verificar sincronização temporal.
- Executar testes preliminares.
- Iniciar e finalizar sessões.
- Monitorar erros.
- Recuperar operações quando possível.
- Salvar e organizar evidências.
- Gerar relatórios compreensíveis.

Utilizar as ferramentas já disponíveis no projeto, incluindo .NET, Npcap, SharpPcap, PacketDotNet, TShark e demais componentes aprovados.

Não instalar ou baixar ferramentas de fontes desconhecidas. Solicitar aprovação quando uma instalação exigir privilégios administrativos, licença, pagamento ou intervenção de segurança do Windows.

### 3. WIZARD DE CAPTURA GUIADA

Criar uma experiência denominada `Guided Capture`.

A tela deverá possuir linguagem simples, instruções curtas e etapas visuais.

O usuário deverá encontrar apenas três ações principais:

- Preparar ambiente.
- Começar captura.
- Finalizar captura.

O restante deverá ser automatizado.

#### Etapa 1 — Preparação

O REA executa todas as verificações técnicas.

Exibe resultados em linguagem simples:

- Jogo encontrado.
- Ferramentas disponíveis.
- Captura de tela preparada.
- Captura de rede preparada.
- Armazenamento disponível.
- Permissões verificadas.

Caso encontre um problema, deverá explicar claramente o que aconteceu e tentar resolver automaticamente.

Exemplo:

"Não consegui acessar a interface de rede necessária. Vou verificar as permissões e tentar corrigir a configuração."

Não apresentar mensagens técnicas incompreensíveis como única explicação.

#### Etapa 2 — Identificação do jogo

O REA detecta processos e janelas.

Se houver mais de uma aplicação possível, apresentar uma lista visual com nome, ícone e executável.

O usuário apenas confirma qual aplicação deseja acompanhar.

Não acessar processos ou modificar a memória do jogo.

#### Etapa 3 — Preparação da sessão

O REA cria automaticamente:

- Identificador da sessão.
- Diretório da sessão.
- Metadados.
- Configuração de captura.
- Registro temporal.
- Arquivos de evidência.
- Associação com o projeto selecionado.

#### Etapa 4 — Instruções ao jogador

Exibir instruções simples e sequenciais.

Exemplo:

"Agora abra o MIR4."

Após detectar o jogo:

"Entre no servidor que deseja analisar."

Após detectar a interface:

"Selecione seu personagem e aguarde o carregamento."

O REA não deverá presumir que uma etapa foi concluída sem verificar evidências visuais suficientes.

Permitir confirmação manual quando o reconhecimento automático não for confiável.

#### Etapa 5 — Captura

Quando tudo estiver preparado, o REA inicia automaticamente:

- Captura visual.
- Registro de eventos.
- Captura de rede passiva.
- Sincronização temporal.
- Monitoramento da aplicação.
- Registro de erros e ocorrências.

O usuário recebe uma indicação clara de que a captura está funcionando.

Durante a sessão, mostrar instruções de atividades que possam gerar evidências úteis.

Exemplos:

- Aguarde o carregamento completo.
- Abra o inventário.
- Consulte uma missão.
- Interaja com um NPC.
- Acesse uma interface do jogo.
- Realize uma atividade normal de combate.
- Retorne à interface principal.

Essas atividades são sugestões de coleta, não comandos automáticos.

O usuário decide quando executá-las.

Não interferir nos comandos do jogador.

#### Etapa 6 — Encerramento

Quando o usuário selecionar "Finalizar captura", o REA:

1. Encerra a captura de forma segura.
2. Finaliza os arquivos.
3. Calcula hashes SHA-256.
4. Verifica integridade.
5. Organiza os dados.
6. Associa evidências.
7. Gera relatório.
8. Salva o histórico.
9. Apresenta o resultado.

### 4. ASSISTENTE DURANTE A CAPTURA

Criar um painel de orientação que permaneça disponível durante a sessão.

Deverá informar:

- O que está sendo capturado.
- O que já foi identificado.
- Quais atividades foram registradas.
- Quais evidências estão faltando.
- Se existe algum problema.
- Qual é a próxima atividade sugerida.

O usuário deverá poder marcar uma atividade como concluída.

O REA também poderá identificar automaticamente a conclusão quando houver evidência visual confiável.

Não apresentar como reconhecido algo que não foi efetivamente detectado.

### 5. MODO MANUAL E MODO IA

Manter dois modos independentes:

**Manual Gameplay:** o usuário joga e o REA executa todo o trabalho técnico de observação, captura, sincronização e organização.

**AI Gameplay:** o agente observa e executa cenários assistidos em ambientes controlados e autorizados, com supervisão humana.

Os dois modos compartilham a infraestrutura técnica.

O modo manual deverá funcionar mesmo que nenhum modelo de IA esteja instalado.

### 6. TRATAMENTO INTELIGENTE DE ERROS

Criar um mecanismo de diagnóstico e recuperação.

Exemplos:

- Jogo fechou: detectar e perguntar se deseja finalizar ou retomar a sessão.
- Captura interrompida: tentar recuperar a operação sem corromper os dados.
- Pouco espaço em disco: alertar e permitir encerramento seguro.
- Ferramenta indisponível: diagnosticar e explicar a situação.
- Perda de sincronização: registrar a ocorrência.
- Falha de OCR: continuar a captura sem OCR e registrar a limitação.

Nunca descartar silenciosamente dados capturados.

Nunca declarar uma captura concluída se o arquivo estiver incompleto ou inválido.

### 7. INTERFACE PARA LEIGOS

Evitar siglas e termos técnicos nas telas principais.

Criar dois níveis:

**Modo Simples:** interface principal para o proprietário, com instruções, indicadores e ações objetivas.

**Modo Técnico:** disponibiliza detalhes de processos, interfaces, filtros, pacotes, protocolos, logs e diagnósticos.

O Modo Técnico deverá existir sem obrigar o usuário a utilizá-lo.

### 8. RELATÓRIO FINAL AUTOMÁTICO

Após cada sessão, apresentar:

- Duração.
- Aplicação monitorada.
- Modo de captura.
- Quantidade de imagens.
- Quantidade de eventos.
- Pacotes capturados.
- Arquivos gerados.
- Integridade dos arquivos.
- Problemas encontrados.
- Atividades realizadas.
- Atividades não concluídas.
- Recomendações para a próxima sessão.

Gerar relatório técnico completo e resumo simplificado.

### 9. REQUISITOS DE IMPLEMENTAÇÃO

- Integrar ao REA Advanced Suite existente.
- Não reescrever as Fases 0 e 1.
- Preservar arquitetura e funcionalidades existentes.
- Implementar incrementalmente nas Fases 2 a 9.
- Criar testes automatizados para cada operação.
- Criar documentação para manutenção.
- Não exigir conhecimento técnico do usuário para operações comuns.
- Não executar operações privilegiadas sem autorização.
- Não implementar mecanismos de evasão de anti-cheat ou manipulação não autorizada do jogo.

### 10. CRITÉRIO FINAL DE ACEITE

Um usuário sem conhecimento técnico deverá conseguir:

1. Abrir o REA.
2. Escolher Manual Gameplay.
3. Clicar em Preparar ambiente.
4. Seguir as instruções apresentadas.
5. Abrir o jogo e jogar normalmente.
6. Deixar o REA capturar e registrar os dados.
7. Finalizar a sessão.
8. Consultar o relatório e as evidências.

Sem abrir terminal, configurar ferramentas ou executar procedimentos técnicos manualmente.

**O REA é responsável pela engenharia e pela captura. O usuário é responsável apenas por seguir as instruções e realizar as atividades de jogo solicitadas.**
