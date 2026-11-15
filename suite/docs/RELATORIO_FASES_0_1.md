# Relatório — REA Advanced Suite, Fases 0 e 1

Data: 15/11/2026 · Branch `feature/rea-advanced-suite` (local, sem push)

## 1. O que foi pedido

1. Instalar o repositório `GabrielMouraKT/REA` na pasta `Desktop\REA ADVANCED SUITE`.
2. Seguir o prompt mestre do REA Advanced Suite, a partir da Fase 0 (auditoria) e da Fase 1 (fundação).
3. Instalar Npcap e Wireshark (autorizado durante a sessão).
4. Criar documentação ensinando a usar a ferramenta inteira.
5. Entregar este relatório.

## 2. O que foi feito e como

### 2.1 Instalação do REA

- **Problema encontrado:** a pasta estava vazia e o Git enxergava o repositório da pasta do usuário
  (`GabrielMouraKT/teste`), que ignora silenciosamente projetos dentro dela.
- **Como:** `git clone` direto na pasta → repositório próprio. Depois `npm ci` + `npm run build:cached`
  (3/3 tarefas OK) e `node scripts/rea.mjs --version` → **6.4.0**.
- **Descoberta:** o REA é um servidor MCP/CLI em TypeScript (fork de `morluto/rea`, MIT), não um app .NET. Por isso a
  suíte .NET foi criada em `suite/` sem alterar o código TypeScript (decisão do dono).

### 2.2 Fase 0 — auditoria

- Inventário do repositório e dos módulos reaproveitáveis (`ghidra/`, `captures/`, `native/`, `dotnet/`, `sqlite/`…).
- Ambiente: .NET SDK 11.0.100, Node 24.14.1, Java 21, Ghidra 12.1.4 presentes; Npcap, Wireshark, WSL, WiX ausentes.
- Branch `feature/rea-advanced-suite` criada. Relatório em `docs/AUDITORIA_FASE0.md`.

### 2.3 Ferramentas instaladas (com autorização)

| Ferramenta             | Como                                                                                                                                                   | Verificação                                     |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------- |
| Wireshark/TShark 4.6.9 | `winget install WiresharkFoundation.Wireshark` (hash conferido pelo winget)                                                                            | `tshark --version`                              |
| Npcap 1.90             | Npcap não está no winget; baixado de npcap.com, **assinatura digital validada** (Nmap Software LLC), instalador aberto para você concluir com loopback | Serviço `npcap` rodando; `NPF_Loopback` listado |

**Teste real de captura:** TShark capturou uma conversa TCP gerada localmente em loopback, salvou PCAPNG, reabriu e
reconstruiu o fluxo (`ping-from-client` / `pong-from-server`).

### 2.4 Fase 1 — fundação

| Camada               | Conteúdo                                                                                                                               |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `REA.Domain`         | Projeto, Ambiente, Registro de atividade, Configuração                                                                                 |
| `REA.Core`           | Serviços de projetos/ambientes/dashboard, validação (FluentValidation, mensagens em português), paginação, contratos, catálogo do menu |
| `REA.Infrastructure` | EF Core + SQLite, migração inicial, repositórios, backup online do SQLite, detecção de ferramentas                                     |
| `REA.Desktop`        | WPF + MVVM, Generic Host, Serilog, tema escuro grafite/azul, menu completo, notificações, diálogos de confirmação                      |

Regras aplicadas: auditoria com antes/depois na mesma transação; exclusão só sem histórico (senão arquivar); sem cascata;
busca parametrizada com escape de curingas; paginação no banco (50/100/200/500); ambiente buscado sempre junto do projeto;
formulários só limpam após salvar; botões travam durante a operação; estado indicado por texto, não só por cor.

### 2.5 Problemas encontrados e resolvidos durante o trabalho

| Problema                                     | Causa                                                      | Solução                                                   |
| -------------------------------------------- | ---------------------------------------------------------- | --------------------------------------------------------- |
| NuGet não resolvia nenhum pacote             | `NuGet.Config` global sem fontes                           | `suite/nuget.config` local (configuração global intocada) |
| `dotnet sln add` com vários projetos falhava | Comportamento do SDK 11                                    | Adicionar um projeto por vez                              |
| `dotnet test` recusado                       | SDK 10+ exige Microsoft.Testing.Platform                   | `suite/global.json`                                       |
| Java e REA CLI apareciam “Não encontrado”    | O REA CLI é servidor MCP e esperava entrada no stdin       | Detector fecha o stdin dos processos                      |
| Npcap mostrava versão 1.10.7                 | Era a versão do libpcap (`wpcap.dll`)                      | Ler versão do `Packet.dll` (1.90)                         |
| Combo “Situação” mostrava o nome da classe   | Template personalizado não repassava o `DisplayMemberPath` | `ContentTemplateSelector` no template                     |
| Colunas da lista de projetos espremidas      | Formulário lateral reduz a largura                         | Larguras mínimas + rolagem horizontal                     |

### 2.6 Descoberta relevante para a Fase 2

`rea inspect-packet-capture` **não funciona no Windows** (exige Linux com `prlimit`). Por isso a captura e a inspeção de
pacotes da suíte serão implementadas em .NET (SharpPcap/PacketDotNet), com TShark como validação cruzada (ADR-006).

## 3. Verificação (resultados reais)

| Verificação                                                              | Resultado                                                                                                                   |
| ------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------- |
| `dotnet build` (avisos tratados como erro)                               | 0 avisos, 0 erros                                                                                                           |
| Testes unitários                                                         | 20/20                                                                                                                       |
| Testes de integração (SQLite real em disco)                              | 15/15                                                                                                                       |
| **Critério de aceite da Fase 1** (cria projeto, fecha, reabre, recupera) | Aprovado (`Project_and_environment_survive_application_restart`)                                                            |
| Interface real                                                           | Aberta e conferida por capturas de tela; fluxo “Novo Projeto → criar → nome duplicado recusado” executado via UI Automation |
| `check-environment.ps1`                                                  | Obrigatórios OK; WSL/Kali e WiX opcionais ausentes                                                                          |
| `publish.ps1` (self-contained)                                           | Gerado (149,5 MB, 451 arquivos); executado sem .NET, criou e migrou o banco                                                 |
| `rea inspect-artifact notepad.exe`                                       | Funcionou (PE, SHA-256, evidência)                                                                                          |

Observação honesta: uma execução de `test.ps1` teve 1 falha enquanto a publicação rodava ao mesmo tempo; não se repetiu
em 6 execuções seguintes (4 com a CPU sob carga). A causa não foi confirmada — registrada em `KNOWN_ISSUES.md` #5.

## 4. Arquivos criados (todos em `suite/`)

| Assunto        | Arquivos                                                                                                                                                                                                                                                 |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Build          | `REA.sln`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, `global.json`, `dotnet-tools.json`, `.gitignore`                                                                                                                         |
| Domínio        | `src/REA.Domain/Entities.cs`                                                                                                                                                                                                                             |
| Aplicação      | `src/REA.Core/{Abstractions,Results,Projects,Environments,Dashboard,Navigation,CoreServiceCollectionExtensions}.cs`                                                                                                                                      |
| Infraestrutura | `src/REA.Infrastructure/{ReaPaths,ReaDbContext,Repositories,SqliteBackupService,ToolDetector,InfrastructureServiceCollectionExtensions}.cs`, `Migrations/*InitialCreate*`                                                                                |
| Interface      | `src/REA.Desktop/{App,MainWindow}.xaml(.cs)`, `Themes/Theme.xaml`, `Views/{ProjectPages,SystemPages,DialogWindow}.xaml`, `ViewModels/*.cs`, `Services/UiServices.cs`, `Converters.cs`, `app.manifest`                                                    |
| Testes         | `tests/REA.UnitTests/CoreTests.cs`, `tests/REA.IntegrationTests/{TestApp,FoundationTests}.cs`                                                                                                                                                            |
| Scripts        | `scripts/{check-environment,build,test,run,publish}.ps1`                                                                                                                                                                                                 |
| Documentação   | `README.md`, `docs/GENERAL/{USER_GUIDE,INSTALLATION,ARCHITECTURE}.md`, `docs/AUDITORIA_FASE0.md`, este relatório, `DEVELOPMENT_STATUS.md`, `IMPLEMENTATION_PLAN.md`, `ARCHITECTURE_DECISIONS.md`, `KNOWN_ISSUES.md`, `FEATURE_MATRIX.md`, `CHANGELOG.md` |

Migração de banco: `InitialCreate` (aditiva; cria `projects`, `environments`, `activity_log`, `app_settings`).

## 5. Pendências

- Fases 2 a 9 (menu já mostra cada módulo com fase e dependências).
- Instalador MSI (WiX ausente) e scripts `install-dependencies`, `package`, `backup`, `restore` (backup/restauração já existem dentro do app).
- Fluxos de Ambientes/Backup/Duplicar ainda não exercitados pela interface (cobertos por testes de integração).
- Push do branch: aguardando sua autorização.
