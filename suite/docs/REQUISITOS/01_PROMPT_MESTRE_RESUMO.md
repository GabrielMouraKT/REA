# Prompt mestre — REA Advanced Suite (resumo fiel por seção)

> O dono enviou o prompt mestre completo em 15/11/2026. Este arquivo preserva todos os requisitos por seção para retomada sem a conversa original.

**Missão:** plataforma Windows profissional, modular e funcional (não só visual) para engenharia reversa, análise de tráfego/arquivos,
documentação de protocolos, launchers, portais, wiki e administração de servidores MMORPG próprios. MIR4 é o primeiro projeto de referência,
sem limitar a ferramenta a ele. Não parar no planejamento; não apresentar funcionalidade fictícia como concluída; documentar dependências.

**Cinco plataformas integradas:** REA Research · Launcher Studio · Portal Studio · Wiki & Knowledge Base · GM & Server Administration —
compartilhando projetos, banco, configurações, arquivos, documentação, registros e evidências.

**Referências:** REA original (preservar e evoluir); Ghidra (headless, funções, strings, xrefs, decompilação, exportação — não recriar o que ele faz);
MIR4 Client Emulator (AlSch092, referência de cliente/rede, **não é servidor**); Wireshark/TShark, Npcap, Fiddler, Protobuf, .NET, WPF,
SharpPcap, PacketDotNet, CommunityToolkit.Mvvm, EF Core, Serilog, WiX, Kali tools. Verificar licença/manutenção antes de adotar.

**Stack:** C# .NET 10 LTS, WPF, MVVM, DI, Hosting, EF Core + SQLite (migrações, índices, integridade, backup, export/import), Serilog,
System.Text.Json, FluentValidation, xUnit. Nada de Electron no desktop. Web: ASP.NET Core (Razor/Blazor), REST. Launcher: WPF separado.

**Interface (6):** dark mode, azul, grafite, produtiva; menu lateral completo (Dashboard, Projects, Research, Comparative Analysis,
Launcher Studio, Portal Studio, Wiki & Knowledge, GM & Server Admin, Evidence, Settings); nenhum menu sem ação.

**Research (7):** Network Capture (interfaces, loopback, BPF, tempo real, PCAP/PCAPNG import/export, limites, memória, stats; não carregar
PCAP gigante inteiro) · Packet Inspector · TCP Stream Explorer (não afirmar reconstrução completa com lacunas) · HTTP/HTTPS Analyzer
(Fiddler/FiddlerCore se licença permitir, TShark, HAR, SAZ; cookies só de sessões próprias; indicar quando cifrado) · Protocol Explorer
(padrões, framing, campos, hipóteses de serialização, anotações versionadas; não presumir Protobuf) · Protobuf Analyzer (decoder, encoder
de teste, parser .proto, wire types, nested, repeated, enums, comparação; não inventar nomes de campo) · Binary Analysis (EXE/DLL/SO/BIN/DAT/PAK:
magic, hashes, arquitetura, strings, recursos, seções, dependências; hex viewer) · File Analysis (formatos, duplicados, comparação binária,
SHA-256, diretórios, manifestos, versões; PAK: identificar formato, headers, índice, compressão, adaptadores por formato, extrair só o seguro e
autorizado, preservar originais) · Ghidra Integration (detectar, configurar, versão, importar, projetos, análise, scripts, analyzeHeadless, logs,
exportar; assíncrono e cancelável) · Kali via WSL2 (detectar, catálogo nmap/tshark/tcpdump/mitmproxy, sem comandos arbitrários, nada contra terceiros).

**Comparative Analysis (8):** perfis de ambiente (nome, tipo, cliente, versão, hashes, launcher, diretório, endpoints, arquivos, capturas,
sessões, descobertas, histórico; sem credenciais como identificador); Client Version Tracker; Protocol Comparison com relatório (diferenças,
semelhanças, não resolvidas, confiança); Evidence Correlation em grafo; descobertas com origem, evidência, data, método, confiança, status
(Confirmado, Provável, Hipótese, Não identificado, Inconclusivo) — **nunca hipótese como fato**.

**MIR4 Research Template (9):** 45 categorias (01 Client Architecture … 45 Unresolved Findings) com documentos, evidências, arquivos, mensagens,
funções, serviços, diagramas, notas e descobertas.

**Launcher Studio (10):** builder (projeto, jogo, publisher, versão, plataforma, saída, template, tema, update source, API, download), visual
editor com preview, arrastar/redimensionar/camadas, painel de propriedades (sem editar XAML); asset manager (PNG/JPG/WEBP/SVG/ICO/GIF/fontes/áudio;
estrutura de pastas definida); update manager (manifesto versionado, SHA-256, retomada, incremental, reparo, assinatura); configuração (API,
site, download, patch, auth, discovery, versões, idioma, região, canal; **sem segredos no executável**); build & release (ZIP, instalador WiX,
manifesto, changelog, assinatura se houver certificado). Launcher-base próprio, independente do MIR4. Para launchers de terceiros, só editar o
que se tem autorização; **não adulterar assinaturas nem contornar integridade**.

**Portal Studio (11):** assistente (jogo, logo, publisher, domínio, cores, idioma, contas, downloads, API, banco, tema, deploy); páginas Home,
Register, Login, Download, News, Events, Game Info, Server Status, Rankings, Account Center, Support, Terms, Privacy, Admin; temas; contas com
adaptadores (cadastro, login, recuperação, verificação de e-mail, perfil); download center sem links inventados; admin do site; deploy (VPS, env,
build, domínio, HTTPS, backup, docs).

**Wiki (12):** categorias (itens, armas, armaduras, acessórios, materiais, consumíveis, moedas, skills, personagens, classes, monstros, bosses,
NPCs, mapas, dungeons, quests, eventos, conquistas, receitas, GM commands, protocolos, opcodes, mensagens, serviços); item com nome, ID, ícone,
imagem, descrição, categoria, raridade, nível, tipo, stack, origem, obtenção, fonte, confiança; filtros e busca; importação CSV/JSON/XML/Excel/
banco/imagens/manifestos com correspondência ID↔imagem revisável (não inventar); cards e detalhe; exportar HTML/JSON/CSV/PDF/Markdown;
Opcode Wiki sem inventar nomes.

**GM & Server Admin (13):** conexões com credenciais via DPAPI/Credential Manager; adaptadores (IServerAdapter, IAccountAdapter, IPlayerAdapter,
ICharacterAdapter, IInventoryAdapter, IItemAdapter, ICurrencyAdapter, IEventAdapter, IBoostAdapter, IDropRateAdapter, IBanAdapter,
IGMCommandAdapter; ex.: MySql, RestApi, CustomMir4, LocalDevelopment) só com API/mecanismo real; opcode de jogo ≠ comando GM; dashboard,
jogadores, itens, moedas, eventos, boosts, drop rate (preview), bans (sem coleta furtiva de HWID, spoofing ou evasão); console com catálogo,
validação, histórico, permissões, confirmação, dry-run, auditoria; Wiki de comandos (não inventar); SQL console somente leitura por padrão.

**Documentação (14):** docs/GENERAL, NETWORK, BINARY, LAUNCHER, PORTAL, GM, MIR4 (lista de arquivos no prompt); nunca apagar descobertas.

**Segurança (15):** sem roubo de credenciais, tokens de terceiros, força bruta, exploração de terceiros, evasão de anti-cheat, falsificação de
cliente, bypass de autenticação, extração furtiva, spoofing de HWID, burla de assinatura de launcher, comandos destrutivos sem confirmação.
Implementar proteção de credenciais, redação de segredos em logs, criptografia de dados sensíveis, controle de acesso, auditoria, backup,
SHA-256, verificação de origem.

**Evidence Manager (16):** ID, projeto, ambiente, categoria, origem, arquivo, SHA-256, data, descrição, descobertas, confiança, status, notas;
importar, exportar, comparar, relacionar, anotar, pesquisar, relatório; nunca alterar originais.

**Dashboard (17):** métricas reais (projetos, ambientes, capturas, pacotes, fluxos, arquivos, binários, mensagens, opcodes, descobertas,
hipóteses, launchers, portais, servidores, comandos, atividades) e gráficos; demo só em modo Demo identificado.

**Automação (18):** fila de tarefas configurável (estatísticas ao importar PCAPNG, hash ao importar binário, manifesto ao importar arquivos,
oferecer Ghidra, documentação de descobertas, estrutura de launcher, manifesto ao publicar, docs de deploy, ícones de catálogo, Wiki de comandos
GM); cancelamento, logs, falhas; nunca ações administrativas críticas automáticas.

**Projetos (19):** criar, abrir, duplicar, arquivar, exportar, importar, backup, restaurar, versionar; sem mistura entre projetos.

**Agentes (20):** até 7 em paralelo se houver suporte; documentos de contexto para continuidade.

**Fases (21):** 0 auditoria · 1 fundação · 2 rede (critério de captura real) · 3 protocolo e binários · 4 comparativa · 5 launcher (criar, editar,
compilar, executar demo) · 6 portal (gerar e rodar demo) · 7 wiki e GM (catálogo demo, busca, ícones, adaptador de laboratório) · 8 MIR4 ·
9 finalização (testes, performance, segurança, MSI, Windows limpo). Branch `feature/rea-advanced-suite`; **sem push sem autorização**.

**Scripts (22):** setup, check-environment, install-dependencies, build, test, publish, package, backup, restore.

**Testes (23):** todas as áreas + erros (arquivo inválido/incompleto/corrompido, captura interrompida, interface indisponível, banco
desconectado, download interrompido, manifesto inválido, integração indisponível, comando rejeitado); mocks não bastam para declarar integração.

**UX (24):** loading, vazio, sucesso, erro, confirmação, feedback, tooltips, atalhos, busca, filtros, paginação, operações longas; notificações
não intrusivas; previews no launcher e no portal.

**Continuidade (25):** DEVELOPMENT_STATUS, IMPLEMENTATION_PLAN, ARCHITECTURE_DECISIONS, KNOWN_ISSUES, FEATURE_MATRIX, CHANGELOG atualizados a
cada fase.

**Aceite (26):** desktop executável e persistente; research com captura real, PCAPNG, inspector, TCP, binários, Ghidra; comparativa; launcher;
portal; wiki; GM; documentação. Recurso só é concluído com implementação + validação + teste.

## Requisitos adicionais do dono (fora do prompt mestre)

- Guia do usuário em **HTML com CSS e JS** (`docs/guia/index.html`), com capítulo de "como usar" acrescentado ao fim de **cada fase**.
- Relatório de cada fase (`docs/RELATORIO_FASE<N>.md`) com o que foi feito e como.
- Bot Lab AI: `02_BOT_LAB_AI.md`.
- REA Autonomous Technical Operator / Guided Capture / Modo Simples × Técnico: `03_AUTONOMOUS_OPERATOR.md`.
