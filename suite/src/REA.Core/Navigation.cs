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
            new("research.capture", "Network Capture", 2, false, "Captura real com Npcap/SharpPcap, filtros BPF, PCAPNG.", "Npcap 1.90 (instalado), SharpPcap, PacketDotNet"),
            new("research.packets", "Packet Inspector", 2, false, "Pacotes com cabeçalhos, flags, hex e ASCII.", "PacketDotNet"),
            new("research.tcp", "TCP Stream Explorer", 2, false, "Reconstrução de fluxos TCP com detecção de lacunas e retransmissões."),
            new("research.http", "HTTP/HTTPS Analyzer", 3, false, "Importação HAR/SAZ e análise via TShark; HTTPS só com chaves próprias.", "TShark 4.6.9 (instalado), Fiddler opcional"),
            new("research.protocol", "Protocol Explorer", 3, false, "Padrões, framing, campos e hipóteses de serialização."),
            new("research.protobuf", "Protobuf Analyzer", 3, false, "Decodificação wire-format, parser .proto, comparação."),
            new("research.binary", "Binary Analysis", 3, false, "PE/ELF, seções, imports, strings, hashes, visualizador hex.", "REA CLI (instalado)"),
            new("research.files", "File Analysis", 3, false, "Formatos, duplicados, manifestos, comparação entre versões."),
            new("research.ghidra", "Ghidra Integration", 3, false, "analyzeHeadless assíncrono, funções, strings, xrefs.", "Ghidra 12.1.4 + Java 21 (instalados)"),
            new("research.kali", "Kali Tools", 3, false, "Detecção de WSL/Kali e catálogo de ferramentas.", "WSL2 + Kali (não instalados)"),
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
        new("EVIDENCE", [
            new("evidence.captures", "Captures", 2, false, "Capturas por projeto com SHA-256."),
            new("evidence.files", "Files", 3, false, "Arquivos importados, originais preservados."),
            new("evidence.findings", "Findings", 4, false, "Descobertas com origem, método e confiança."),
            new("evidence.reports", "Reports", 4, false, "Relatórios exportáveis."),
            new(ActivityLog, "Atividades", 1, true, "Auditoria legível de todas as alterações."),
        ]),
        new("SETTINGS", [
            new("settings.integrations", "Integrations", 3, false, "Configuração de caminhos do REA CLI, Ghidra e TShark."),
            new(SettingsDatabase, "Database", 1, true, "Arquivo, tamanho e migrações aplicadas."),
            new(SettingsTools, "Tools", 1, true, "Detecção real das ferramentas externas instaladas."),
            new("settings.appearance", "Appearance", 9, false, "Tema escuro é o padrão; temas adicionais na finalização."),
            new(SettingsBackup, "Backup", 1, true, "Backup manual, automático diário e restauração."),
        ]),
    ];

    public static IEnumerable<NavItem> AllItems => Sections.SelectMany(s => s.Items);

    public static NavItem? Find(string key) => AllItems.FirstOrDefault(i => i.Key == key);
}
