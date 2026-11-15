# REA ADVANCED SUITE — CLAUDE CONTROL CENTER

> Requisito do dono, recebido em 15/11/2026. Texto integral preservado abaixo. Status e decisões em `IMPLEMENTATION_PLAN.md`.

## Integração nativa do Claude Code como agente administrador e desenvolvedor da plataforma

### 1. OBJETIVO

Implementar o Claude Control Center, um módulo integrado ao REA Advanced Suite que permita ao usuário conversar com Claude, enviar instruções em linguagem natural e delegar tarefas técnicas diretamente pela interface.

O Claude deverá atuar como assistente técnico e agente operacional do REA, podendo analisar, planejar, desenvolver, corrigir, testar e documentar funcionalidades.

O objetivo é eliminar a necessidade de copiar prompts manualmente para uma ferramenta externa em cada tarefa.

O usuário deverá conseguir trabalhar por conversação contínua, mantendo contexto e histórico.

### 2. TECNOLOGIA

Avaliar e utilizar prioritariamente:

- Claude Code CLI.
- Claude Agent SDK.
- MCP para integração com ferramentas do REA.
- .NET como camada principal de orquestração.
- Named Pipes ou comunicação local segura.
- JSON versionado para troca de mensagens.
- Serilog para auditoria e logs.

Antes da implementação, verificar versões, compatibilidade, requisitos de autenticação, licenciamento e custos.

Não confundir assinatura Claude Pro com créditos de API. Identificar claramente o mecanismo de autenticação e cobrança utilizado.

### 3. INTERFACE

Criar aba principal:

CLAUDE CONTROL CENTER

Submenus:

- Chat.
- Project Explorer.
- Task Manager.
- Execution Monitor.
- Terminal.
- Git Manager.
- Agent Manager.
- Session History.
- Permissions.
- Settings.

### 4. CHAT INTELIGENTE

Criar interface de conversação contínua.

Permitir:

- Enviar instruções em linguagem natural.
- Anexar arquivos.
- Enviar prompts longos.
- Continuar conversas anteriores.
- Criar novas sessões.
- Interromper execução.
- Solicitar explicações.
- Solicitar planejamento antes de executar.
- Consultar resultados.
- Reenviar instruções para refinamento.

Preservar o contexto de cada projeto e sessão.

### 5. GERENCIAMENTO DE PROJETOS

Permitir selecionar um workspace.

Exemplos:

- REA Advanced Suite.
- MIR4 Research.
- Network Analyzer.
- Bot Lab AI.
- Launcher Manager.
- Wiki Builder.

O Claude deverá trabalhar exclusivamente nos diretórios autorizados para cada workspace.

Não ler o projeto inteiro indiscriminadamente. Identificar os arquivos relevantes antes de iniciar uma análise.

### 6. GERENCIAMENTO DE TAREFAS

Criar fluxo de execução:

1. Receber solicitação.
2. Interpretar objetivo.
3. Consultar contexto do projeto.
4. Elaborar plano.
5. Apresentar plano ao usuário quando necessário.
6. Solicitar aprovação para operações protegidas.
7. Executar.
8. Acompanhar progresso.
9. Executar testes.
10. Apresentar resultados.
11. Atualizar documentação.

Cada tarefa deverá possuir:

- ID.
- Nome.
- Descrição.
- Projeto.
- Status.
- Prioridade.
- Data de criação.
- Início e conclusão.
- Arquivos alterados.
- Comandos executados.
- Testes realizados.
- Resultado.
- Erros.
- Histórico.

### 7. EXECUTION MONITOR

Exibir em tempo real:

- Atividade atual.
- Etapa da tarefa.
- Agente responsável.
- Arquivos analisados.
- Arquivos modificados.
- Comandos em execução.
- Saída do terminal.
- Testes.
- Erros.
- Tempo de execução.

Permitir pausar, cancelar e retomar tarefas quando tecnicamente suportado.

### 8. MULTI-AGENT MANAGER

Permitir delegação para subagentes especializados.

Exemplos:

- Software Architect.
- Backend Developer.
- Frontend Developer.
- Database Engineer.
- Reverse Engineering Analyst.
- Network Analyst.
- QA Engineer.
- Documentation Specialist.
- Security Reviewer.

Utilizar seleção automática de agentes conforme a tarefa.

Limitar inicialmente a sete terminais/processos simultâneos, respeitando o limite operacional definido para o ambiente.

Evitar agentes duplicando trabalho ou modificando simultaneamente os mesmos arquivos sem coordenação.

### 9. INTEGRAÇÃO COM REA

Criar ferramentas MCP controladas para:

- Consultar status dos módulos.
- Consultar implementação das fases.
- Consultar documentação.
- Consultar sessões de captura.
- Consultar relatórios.
- Consultar inventário de ferramentas.
- Criar tarefas.
- Atualizar tarefas.
- Solicitar execução de testes.
- Consultar resultados.

O Claude deverá utilizar os serviços oficiais do REA.

Não permitir acesso direto e descontrolado ao SQLite.

Não permitir que o Claude modifique configurações internas protegidas sem autorização.

### 10. PERMISSÕES E SEGURANÇA

Criar níveis:

**Leitura:** consultar arquivos, documentação e relatórios.

**Planejamento:** analisar e propor modificações.

**Execução controlada:** editar arquivos e executar testes em workspace autorizado.

**Operações protegidas:** exigir confirmação explícita.

Operações protegidas incluem:

- Excluir diretórios.
- Alterar configurações críticas.
- Executar comandos administrativos.
- Instalar dependências.
- Modificar banco de dados.
- Executar migrations.
- Alterar branches e histórico Git.
- Fazer commits e push.
- Publicar versões.
- Modificar arquivos fora do workspace autorizado.

Nunca implementar modo irrestrito de execução como padrão.

Registrar todas as ações.

### 11. SESSÕES E CONTEXTO

Persistir:

- Histórico da conversa.
- Identificador da sessão Claude.
- Projeto associado.
- Tarefas executadas.
- Decisões técnicas.
- Resultados.
- Arquivos modificados.
- Estado de execução.

Permitir retomar conversas após fechar e reabrir o REA.

Implementar recuperação de tarefas interrompidas, distinguindo continuidade da conversa e recuperação do estado real dos arquivos.

### 12. CONTROLE DE CUSTOS

Exibir:

- Modelo utilizado.
- Consumo informado pelo provedor, quando disponível.
- Tempo de execução.
- Quantidade de chamadas.
- Limites configurados.
- Alertas de consumo.

Não inventar valores de custo quando a informação não estiver disponível.

Permitir configurar limites e avisos.

### 13. TESTES

Validar:

- Inicialização do Claude.
- Autenticação.
- Envio de prompts.
- Respostas em streaming.
- Continuidade de sessão.
- Interrupção.
- Recuperação.
- Permissões.
- Execução de comandos.
- Isolamento de workspace.
- Integração MCP.
- Logs.
- Falhas de conexão.
- Encerramento correto do REA.

### 14. DOCUMENTAÇÃO

Criar:

docs/CLAUDE_CONTROL_CENTER/README.md
docs/CLAUDE_CONTROL_CENTER/ARCHITECTURE.md
docs/CLAUDE_CONTROL_CENTER/INSTALLATION.md
docs/CLAUDE_CONTROL_CENTER/CONFIGURATION.md
docs/CLAUDE_CONTROL_CENTER/SECURITY.md
docs/CLAUDE_CONTROL_CENTER/USER_GUIDE.md

Atualizar o planejamento geral e a matriz de funcionalidades.

### 15. REGRAS DE IMPLEMENTAÇÃO

- Preservar as Fases 0 e 1 já aprovadas.
- Integrar incrementalmente nas Fases 2–9.
- Não reescrever o REA.
- Não duplicar serviços.
- Não modificar funcionalidades aprovadas sem justificativa.
- Criar testes para cada integração.
- Fazer backup antes de alterações estruturais.
- Manter documentação atualizada.
- Não executar git push sem autorização expressa.

### 16. CRITÉRIOS DE ACEITE

O usuário deverá conseguir abrir o REA, selecionar um projeto, conversar com Claude, solicitar tarefas, acompanhar execução, aprovar operações protegidas, consultar resultados e continuar uma sessão anterior.

O Claude Control Center deverá funcionar como uma interface de gerenciamento de desenvolvimento e operação do próprio REA, sem substituir a arquitetura central da plataforma.
