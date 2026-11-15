# Relatório — Fase 2 (Network Research)

Data: 15/11/2026 · Branch `feature/rea-advanced-suite` (local, sem push) · Versão 0.2.0

## 1. O que foi entregue

| Módulo                  | O que faz                                                                                                                                                                                                                              |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Network Capture**     | Captura real via Npcap + SharpPcap, interfaces (incl. loopback), filtro BPF validado antes de iniciar, limites de pacotes/tamanho/tempo, estatísticas ao vivo, pacotes descartados, gravação PCAPNG contínua (válida mesmo após queda) |
| **Evidence › Captures** | Lista paginada por projeto, importação de PCAP/PCAPNG (cópia + SHA-256 conferido, original intocado), detalhes, validação com TShark, reanálise, exportação, mostrar na pasta                                                          |
| **Packet Inspector**    | Leitura paginada sem carregar o arquivo inteiro, filtros (protocolo, endereço, porta, texto nos bytes, só com dados), camadas decodificadas, hex+ASCII do quadro e do payload, exportação CSV                                          |
| **TCP Stream Explorer** | Reconstrução por conexão com reordenação, retransmissões, sobreposições, lacunas (inclusive bytes confirmados por ACK e não capturados), estados Completo / Sem lacunas parcial / Incompleto, linha do tempo, exportação de bytes      |
| **Dashboard**           | Capturas, pacotes analisados e fluxos TCP reais; contagens carregam antes da detecção de ferramentas                                                                                                                                   |
| **Regras de evidência** | Ambiente/projeto com capturas não podem ser excluídos; capturas não têm exclusão; captura interrompida por queda é recuperada na próxima abertura                                                                                      |

## 2. Como foi feito

- **Leitor/gravador PCAPNG próprios** (`REA.PacketAnalyzer/CaptureFiles.cs`): o libpcap/SharpPcap só grava pcap clássico. Implementados SHB/IDB/EPB (gravação) e SHB/IDB/EPB/SPB/OPB + pcap clássico µs/ns nas duas ordens de bytes (leitura). Acesso aleatório por deslocamento para paginação.
- **Decodificação** com PacketDotNet 1.4.8 (MPL-2.0); **captura** com SharpPcap 6.3.1 (MIT). Licenças conferidas antes de adotar.
- **Reconstrução TCP** própria (`TcpReassembler.cs`), sem dependências.
- **Validação cruzada** com TShark (`TsharkCrossCheck.cs`): contagem de quadros, conversas (`conv,tcp`) e conteúdo de cada fluxo (`follow,tcp,raw`).
- **Banco**: migração aditiva `AddCaptures` (tabela `captures`, FKs sem cascata, índices por projeto/data, ambiente e situação).

## 3. Critério obrigatório da Fase 2 — resultado real

Teste `LiveLoopbackCaptureTests.Real_loopback_capture_saves_reopens_reassembles_and_matches_tshark` (tráfego real gerado no teste):

1. Captura real no loopback (Npcap 1.90) — **ok**
2. PCAPNG salvo — **ok**
3. Captura fechada — **ok**
4. Arquivo reaberto (`CaptureDocument`) — **ok**, sem truncamento
5. Pacotes visualizados — **ok** (todos TCP, SYN presente)
6. Fluxo reconstruído — **ok**: 3009 bytes cliente→servidor e resposta idênticos aos enviados, estado **Completo**
7. Comparação com TShark 4.6.9 — **ok**: pacotes, conversas e conteúdo iguais

Também validado pela interface real (UI Automation + capturas de tela): captura de 11 pacotes, abertura no Inspector, fluxo "Completo" com `PING-REA-UI`, validação TShark `[OK]` em tudo.

## 4. Testes

| Projeto                                                                                       | Resultado              |
| --------------------------------------------------------------------------------------------- | ---------------------- |
| REA.UnitTests                                                                                 | 20/20                  |
| REA.NetworkTests (formatos, decoder, reconstrução, filtros, captura real)                     | 25/25                  |
| REA.IntegrationTests (serviço + SQLite real, inclui captura ao vivo e recuperação após queda) | 21/21                  |
| **Total**                                                                                     | **66/66, 0 ignorados** |

Build: 0 avisos, 0 erros (avisos tratados como erro).

## 5. Problemas encontrados e corrigidos

| Problema                                      | Correção                                                                                                       |
| --------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| Leitura paginada de PCAPNG retornava vazio    | Leitor novo não conhecia as interfaces (IDB) anteriores ao pacote → varredura de cabeçalhos até o deslocamento |
| Limite de pacotes podia gravar pacotes a mais | Limite verificado dentro da trava de escrita                                                                   |
| Dashboard mostrava 0 enquanto carregava       | Contagens primeiro, "…" até chegar, ferramentas depois ("Detectando…")                                         |
| Ações espremiam a tabela de capturas          | Barra de ações para a captura selecionada + duplo clique                                                       |
| Texto de ajuda sobreposto à lista de pacotes  | Aparece só quando a lista está vazia                                                                           |

## 6. Arquivos (todos em `suite/`)

| Assunto      | Arquivos                                                                                                                                       |
| ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Domínio/Core | `REA.Domain/Captures.cs`, `REA.Core/{CaptureContracts,CaptureService,Dashboard,Navigation,Projects,Environments}.cs`                           |
| Análise      | `REA.PacketAnalyzer/{CaptureFiles,PacketDecoder,TcpReassembler,CaptureDocument,TsharkCrossCheck,PacketAnalyzerServiceCollectionExtensions}.cs` |
| Captura      | `REA.NetworkCapture/SharpPcapCaptureEngine.cs`                                                                                                 |
| Infra        | `REA.Infrastructure/{CaptureRepository,ReaDbContext,ReaPaths}.cs`, migração `AddCaptures`                                                      |
| Interface    | `ViewModels/{NetworkCaptureViewModel,CapturesViewModel,PacketViewModels}.cs`, `Views/ResearchPages.xaml`, ajustes em Theme/SystemPages/App     |
| Testes       | `tests/REA.NetworkTests/*`, `tests/REA.IntegrationTests/CaptureServiceTests.cs`                                                                |
| Docs         | `docs/guia/` (guia HTML), `docs/BOT_LAB/README.md`, este relatório, arquivos de continuidade                                                   |

## 7. Pendências

- Pausar captura: não implementado (libpcap não pausa; equivalente seria parar e iniciar outra) — registrado em KNOWN_ISSUES.
- HTTP/HTTPS Analyzer, Protocol Explorer, Protobuf e binários: Fase 3.
- Bot Lab AI: planejado nas Fases 4 e 7 (ver `docs/BOT_LAB/README.md`).
