namespace REA.Core;

public sealed record NavItem(string Key, string Title, int Phase, bool Implemented, string Description, string? Dependencies = null);

public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);

/// <summary>
/// Menu completo da seção 6.1. Itens com <see cref="NavItem.Implemented"/> = false abrem a tela de status do
/// módulo (fase prevista, dependências), nunca uma tela falsa.
/// </summary>
public static class NavigationCatalog
{
    public const string Dashboard = "dashboard";
    public const string ProjectsNew = "projects.new";
    public const string ProjectsList = "projects.list";
    public const string ProjectsEnvironments = "projects.environments";
    public const string SettingsDatabase = "settings.database";
    public const string SettingsTools = "settings.tools";
    public const string SettingsBackup = "settings.backup";
    public const string ActivityLog = "evidence.activity";
    public const string ResearchCapture = "research.capture";
    public const string ResearchPackets = "research.packets";
    public const string ResearchTcp = "research.tcp";
    public const string EvidenceCaptures = "evidence.captures";
    public const string ResearchHttp = "research.http";
    public const string ResearchProtocol = "research.protocol";
    public const string ResearchProtobuf = "research.protobuf";
    public const string ResearchBinary = "research.binary";
    public const string ResearchFiles = "research.files";
    public const string ResearchGhidra = "research.ghidra";
    public const string ResearchKali = "research.kali";
    public const string EvidenceFiles = "evidence.files";
    public const string SettingsIntegrations = "settings.integrations";
    public const string GitDashboard = "git.dashboard";
    public const string GitAccounts = "git.accounts";
    public const string GitRepositories = "git.repositories";
    public const string GitCommit = "git.commit";
    public const string GitBranches = "git.branches";
    public const string GitSync = "git.sync";
    public const string GitHistory = "git.history";

    public static IReadOnlyList<NavSection> Sections { get; } =
    [
        new("DASHBOARD", [
            new(Dashboard, "Dashboard", 1, true, "Indicadores reais do banco local e atividades recentes."),
        ]),
        new("PROJETOS", [
            new(ProjectsNew, "Novo Projeto", 1, true, "Cria um projeto de investigação isolado."),
            new(ProjectsList, "Meus Projetos", 1, true, "Lista, edita, duplica, arquiva e exclui projetos."),
            new(ProjectsEnvironments, "Ambientes", 1, true, "Perfis de ambiente por projeto (cliente, versão, diretório)."),
        ]),
        new("RESEARCH", [
            new(ResearchCapture, "Network Capture", 2, true, "Captura real com Npcap/SharpPcap, filtros BPF, PCAPNG.", "Npcap 1.90 (instalado), SharpPcap, PacketDotNet"),
            new(ResearchPackets, "Packet Inspector", 2, true, "Pacotes com cabeçalhos, flags, hex e ASCII.", "PacketDotNet"),
            new(ResearchTcp, "TCP Stream Explorer", 2, true, "Reconstrução de fluxos TCP com detecção de lacunas e retransmissões."),
            new(ResearchHttp, "HTTP/HTTPS Analyzer", 3, true, "HTTP de capturas (via TShark) ou de arquivos HAR; HTTPS aparece como cifrado, credenciais ocultadas.", "TShark (Wireshark)"),
            new(ResearchProtocol, "Protocol Explorer", 3, true, "Hipóteses de enquadramento, opcodes candidatos e corpo Protobuf sobre fluxos TCP."),
            new(ResearchProtobuf, "Protobuf Analyzer", 3, true, "Decodificação wire-format sem .proto, rótulos com .proto e montador de mensagens de teste."),
            new(ResearchBinary, "Binary Analysis", 3, true, "PE/ELF, seções, imports, exports, strings, hashes e visualizador hex."),
            new(ResearchFiles, "File Analysis", 3, true, "Inventário de pastas, duplicados, manifestos e comparação entre versões."),
            new(ResearchGhidra, "Ghidra Integration", 3, true, "Análise headless assíncrona e cancelável: funções, externos e strings.", "Ghidra + Java 21 (do usuário)"),
            new(ResearchKali, "Kali Tools", 3, true, "Detecção de WSL/Kali e verificação de um catálogo fixo de ferramentas.", "WSL2 + distribuição Kali"),
        ]),
        new("COMPARATIVE ANALYSIS", [
            new("compare.environments", "Environment Profiles", 4, false, "Perfis completos com hashes, endpoints e histórico."),
            new("compare.clients", "Client Comparison", 4, false, "Version tracker e diferenças entre clientes."),
            new("compare.protocols", "Protocol Comparison", 4, false, "Relatório de diferenças com nível de confiança."),
            new("compare.files", "File Comparison", 4, false, "Comparação binária e de manifestos."),
            new("compare.timeline", "Session Timeline", 4, false, "Linha do tempo de sessões e descobertas."),
        ]),
        new("LAUNCHER STUDIO", [
            new("launcher.builder", "Launcher Builder", 5, false, "Assistente de projeto de launcher próprio."),
            new("launcher.editor", "Visual Editor", 5, false, "Editor visual com preview em tempo real."),
            new("launcher.assets", "Asset Manager", 5, false, "Biblioteca de imagens, fontes e áudio."),
            new("launcher.config", "Configuration", 5, false, "Endpoints, canais e versões mínimas."),
            new("launcher.updates", "Update Manager", 5, false, "Manifestos versionados com SHA-256 e retomada."),
            new("launcher.integrity", "Integrity Manager", 5, false, "Verificação e reparo de arquivos."),
            new("launcher.release", "Build & Release", 5, false, "Compilação, ZIP, manifesto e instalador.", "WiX Toolset (não instalado)"),
        ]),
        new("PORTAL STUDIO", [
            new("portal.builder", "Website Builder", 6, false, "Gera portal ASP.NET Core a partir de um assistente."),
            new("portal.accounts", "Account Portal", 6, false, "Cadastro, login e recuperação via adaptadores."),
            new("portal.downloads", "Download Center", 6, false, "Downloads, versões, requisitos e espelhos."),
            new("portal.news", "News & Events", 6, false, "Notícias e eventos do portal."),
            new("portal.api", "API Configuration", 6, false, "Adaptadores de autenticação e dados."),
            new("portal.deploy", "Deployment", 6, false, "Scripts e instruções de implantação."),
        ]),
        new("WIKI & KNOWLEDGE", [
            new("wiki.items", "Item Wiki", 7, false, "Catálogo de itens com ícones, filtros e busca."),
            new("wiki.equipment", "Equipment", 7, false, "Armas, armaduras e acessórios."),
            new("wiki.monsters", "Monsters", 7, false, "Monstros e chefes."),
            new("wiki.npcs", "NPCs", 7, false, "NPCs."),
            new("wiki.maps", "Maps", 7, false, "Mapas e masmorras."),
            new("wiki.skills", "Skills", 7, false, "Habilidades."),
            new("wiki.quests", "Quests", 7, false, "Missões."),
            new("wiki.events", "Events", 7, false, "Eventos."),
            new("wiki.protocol", "Protocol Wiki", 7, false, "Mensagens e serviços documentados."),
            new("wiki.opcodes", "Opcode Database", 7, false, "Opcodes com evidência e confiança."),
            new("wiki.gm", "GM Command Wiki", 7, false, "Comandos documentados por adaptador."),
        ]),
        new("GM & SERVER ADMIN", [
            new("gm.connections", "Server Connections", 7, false, "Conexões com credenciais protegidas por DPAPI."),
            new("gm.dashboard", "Dashboard", 7, false, "Status do servidor via adaptador."),
            new("gm.players", "Player Management", 7, false, "Jogadores, quando o adaptador suportar."),
            new("gm.characters", "Character Management", 7, false, "Personagens, quando o adaptador suportar."),
            new("gm.items", "Item Management", 7, false, "Envio/remoção de itens com confirmação."),
            new("gm.currency", "Currency Management", 7, false, "Saldos e ajustes com motivo."),
            new("gm.events", "Event Management", 7, false, "Eventos do servidor."),
            new("gm.boosts", "Boost Management", 7, false, "Multiplicadores com vigência."),
            new("gm.drops", "Drop Rate Management", 7, false, "Taxas de drop com preview."),
            new("gm.bans", "Ban Management", 7, false, "Banimentos suportados pelo servidor."),
            new("gm.audit", "Logs & Audit", 7, false, "Auditoria de operações administrativas."),
            new("gm.console", "Command Console", 7, false, "Console com validação, dry-run e confirmação."),
        ]),
        new("BOT LAB AI", [
            new("botlab.dashboard", "Dashboard", 4, false, "Sessão atual: modo, processo, PID, tempo, FPS, rede, pacotes, screenshots, eventos e espaço em disco."),
            new("botlab.manual", "Manual Gameplay", 4, false, "Você joga normalmente; o REA registra tela, eventos marcados por você e tráfego (passivo). Não exige IA.",
                "Network Capture (Fase 2, pronto), captura de tela do Windows"),
            new("botlab.ai", "AI Gameplay", 7, false, "Agente visual com execução assistida de cenários, SOMENTE em ambientes de laboratório próprios e com aprovação humana de cada ação.",
                "Processo auxiliar Python (Named Pipes, JSON versionado); referências AI.game.agent e Pixeler a confirmar"),
            new("botlab.observer", "Live Observer", 7, false, "Observação visual em tempo real sem executar ações; UNKNOWN_STATE quando não houver confiança."),
            new("botlab.vision", "Visual Recognition", 7, false, "Cadastro de elementos visuais, OCR e estados da interface."),
            new("botlab.scenarios", "Scenario Builder", 7, false, "Cenários de teste com passos, validações e pontos de aprovação."),
            new("botlab.recorder", "Session Recorder", 4, false, "Gravação sincronizada (relógio monotônico + UTC) de tela, eventos, pacotes e logs."),
            new("botlab.history", "Session History", 4, false, "Sessões por data, projeto, aplicação e modo; abrir timeline; comparar; exportar."),
            new("botlab.evidence", "Evidence Explorer", 4, false, "PCAPNG, screenshots, logs, relatórios, metadados, hashes e anotações por sessão."),
            new("botlab.agent", "Agent Configuration", 7, false, "Modelos, limites e permissões do agente; funciona sem Ollama no modo observação."),
        ]),
        new("GIT CONTROL CENTER", [
            new(GitDashboard, "Dashboard", 3, true, "Todos os repositórios dos projetos com o estado de cada um.", "Git (instalado)"),
            new(GitAccounts, "Accounts", 3, true, "Conta do GitHub conectada pelo GitHub CLI (sem senha nem token no REA).", "GitHub CLI (gh)"),
            new(GitRepositories, "Repositories", 3, true, "Vincular, iniciar, clonar e criar repositórios no GitHub."),
            new(GitCommit, "Commit Manager", 3, true, "Alterações, diferenças, seleção de arquivos e commit com verificação de segredos."),
            new(GitBranches, "Branch Manager", 3, true, "Criar, trocar, renomear, excluir com proteção e fazer merge."),
            new(GitSync, "Remote Sync", 3, true, "Buscar, receber e enviar com resumo antes; nunca força."),
            new("git.pulls", "Pull Requests", 4, false, "Criar e acompanhar Pull Requests no GitHub.", "GitHub CLI (gh)"),
            new("git.issues", "Issues", 4, false, "Criar e atualizar Issues no GitHub.", "GitHub CLI (gh)"),
            new("git.releases", "Releases", 4, false, "Tags e Releases no GitHub.", "GitHub CLI (gh)"),
            new(GitHistory, "Git History", 3, true, "Histórico de commits com detalhes de cada um."),
            new("git.settings", "Git Settings", 4, false, "Nome/e-mail do autor e preferências do Git."),
        ]),
        new("EVIDENCE", [
            new(EvidenceCaptures, "Captures", 2, true, "Capturas por projeto com SHA-256."),
            new(EvidenceFiles, "Files", 3, true, "Arquivos importados com SHA-256; originais preservados."),
            new("evidence.findings", "Findings", 4, false, "Descobertas com origem, método e confiança."),
            new("evidence.reports", "Reports", 4, false, "Relatórios exportáveis."),
            new(ActivityLog, "Atividades", 1, true, "Auditoria legível de todas as alterações."),
        ]),
        new("SETTINGS", [
            new(SettingsIntegrations, "Integrations", 3, true, "Pasta do Ghidra e ferramentas detectadas."),
            new(SettingsDatabase, "Database", 1, true, "Arquivo, tamanho e migrações aplicadas."),
            new(SettingsTools, "Tools", 1, true, "Detecção real das ferramentas externas instaladas."),
            new("settings.appearance", "Appearance", 9, false, "Tema escuro é o padrão; temas adicionais na finalização."),
            new(SettingsBackup, "Backup", 1, true, "Backup manual, automático diário e restauração."),
        ]),
    ];

    public static IEnumerable<NavItem> AllItems => Sections.SelectMany(s => s.Items);

    public static NavItem? Find(string key) => AllItems.FirstOrDefault(i => i.Key == key);
}
