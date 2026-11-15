# REA ADVANCED SUITE — BOT LAB AI (prompt complementar do dono, 15/11/2026)

Manual Gameplay + AI Gameplay Agent + Session Intelligence. Aplicação de referência: MIR4. Windows/.NET/WPF; motor visual em Python.
Plano de execução: `docs/BOT_LAB/README.md` (Fase 4: manual; Fase 7: IA).

## 1. Objetivo

Módulo de observação, automação assistida e captura de sessões. Dois modos com a mesma infraestrutura (Screen Capture, Computer Vision,
OCR, Event Recorder, Network Capture, Session Recorder, Evidence Manager, Session Intelligence):

- **MANUAL GAMEPLAY** — o usuário joga e o REA acompanha tudo automaticamente.
- **AI GAMEPLAY** — o agente observa, interpreta e executa cenários de teste assistidos.

**Regra fundamental: não exigir automação para capturar uma sessão.** O usuário joga normalmente enquanto o REA registra.

## 2. Interface — aba BOT LAB AI

Submenus: Dashboard, Manual Gameplay, AI Gameplay, Live Observer, Visual Recognition, Scenario Builder, Session Recorder, Session History,
Evidence Explorer, Agent Configuration. Tela inicial com as duas opções:

- Manual: selecionar aplicação e processo, iniciar observação, iniciar captura de rede, registrar screenshots, marcar eventos, encerrar.
- IA: selecionar aplicação, carregar cenário, iniciar agente, acompanhar interpretação visual, aprovar ações, pausar, interromper, encerrar.

## 3. Manual Gameplay (prioritário)

- Não interferir nos comandos nem na comunicação do jogo; trabalhar em segundo plano.
- **Aplicação:** detectar processos, selecionar janela, executável, PID, título, resolução, detectar encerramento. **Não injetar código.**
- **Captura visual:** tela periódica, screenshots, mudanças visuais, OCR quando necessário, elementos cadastrados, estados da interface,
  timestamps, FPS e resolução configuráveis.
- **Eventos manuais** (anotações do usuário, não conclusões sobre pacotes): login, seleção de personagem, movimentação, combate, habilidade,
  interação, missão, inventário, NPC, mudança de mapa, morte, ressurreição, logout, personalizado; observações e screenshots associados;
  **atalhos configuráveis que não interceptam nem alteram comandos do jogo**.
- **Observação automática:** sem executar ações; UNKNOWN_STATE quando sem confiança; funciona sem Ollama/modelos.
- **Rede:** integrar ao Network Analyzer (interface, adaptador Npcap, iniciar, pausar quando possível, encerrar, PCAPNG, metadados, SHA-256,
  associar à sessão); passiva.
- **Sincronização:** relógio monotônico + UTC para screenshots, eventos manuais, eventos visuais, pacotes, fluxos, logs; timeline única.

## 4. AI Gameplay

Referências: **AI.game.agent** (arquitetura) e **Pixeler** (visão computacional) — links ainda não informados.
Captura visual, reconhecimento, OCR, interpretação de estados, contexto, máquina de estados, Scenario Builder, execução assistida, validação.
Ações interativas sob aprovação humana em ambientes de teste autorizados. **Não** implementar farm/combate/progressão em servidores públicos
de terceiros nem contornar anti-cheat.

## 5. Session Intelligence

Correlacionar tela, eventos do usuário, reconhecimentos do agente, horários, pacotes do intervalo e fluxos ativos. **Não presumir que um pacote
corresponde a uma ação sem evidência.** Selecionar um evento na timeline e ver os dados associados.

## 6. Dashboard de sessões

Modo, aplicação, PID, tempo, FPS, estado visual, estado do agente, interface de rede, captura ativa, pacotes, screenshots, eventos,
tamanho dos arquivos, espaço disponível.

## 7. Session History

Listar; filtrar por data, projeto, aplicação, modo; duração; abrir timeline; consultar capturas; comparar sessões; exportar evidências.

## 8. Evidence Explorer

Por sessão: PCAPNG, screenshots, logs, relatórios, metadados, hashes, anotações; exportação; integridade e histórico de alterações.

## 9. Implementação técnica

Preservar REA.Domain/Core/Infrastructure/Desktop. Serviços separados para manual e IA com infraestrutura comum. Python como processo auxiliar,
comunicação por **Named Pipes com JSON versionado**. **Python sem acesso direto ao SQLite.** Não duplicar funcionalidades.

## 10. Testes obrigatórios

1 captura de tela sem interação · 2 captura durante uso normal · 3 eventos manuais · 4 atalhos configuráveis · 5 identificação de processos ·
6 rede simultânea · 7 sincronização temporal · 8 encerramento correto · 9 recuperação após falha · 10 integridade · 11 consulta histórica ·
12 exportação · 13 sem modelos de IA · 14 com agente visual · 15 comparação manual × assistida. Testes automatizados com aplicações simuladas para CI.

## 11. Documentação

`docs/BOT_LAB/{README,MANUAL_GAMEPLAY,AI_GAMEPLAY,SESSION_RECORDER,SESSION_INTELLIGENCE,NETWORK_CORRELATION,USER_GUIDE}.md` + atualizar
IMPLEMENTATION_PLAN, FEATURE_MATRIX, DEVELOPMENT_STATUS, ARCHITECTURE_DECISIONS, CHANGELOG, KNOWN_ISSUES.

## 12. Critérios de aceite

Jogar manualmente sem interferência; captura visual durante o jogo; rede simultânea; eventos marcados manualmente; timeline sincronizada;
agente observa e reconhece estados; modo IA em cenários autorizados; sessões armazenadas e recuperadas; evidências exportáveis; testes
aprovados; documentação completa.

## 13. Instruções finais

Os dois modos são funcionalidades de primeira classe; o manual não é secundário; IA não é exigida para capturar; não modificar Fases 0 e 1;
integrar nas Fases 2 a 9; não reescrever sem justificativa; build + testes + validação de interface; **sem git push sem autorização**.
