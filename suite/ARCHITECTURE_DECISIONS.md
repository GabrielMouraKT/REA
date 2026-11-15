# Registro de decisões de arquitetura (ADR)

## ADR-001 — Suíte .NET dentro de `suite/` no repositório REA (2026-11-15)

**Contexto:** o repositório REA é um projeto TypeScript (MCP/CLI) que já ocupa `src/`.
**Decisão:** solução `suite/REA.sln` isolada; o código TypeScript não é alterado. (Escolha do dono.)
**Consequência:** o desktop usa o motor REA pelo CLI/MCP; os dois evoluem no mesmo repositório.

## ADR-002 — Alvo `net10.0` com `RollForward=LatestMajor` (2026-11-15)

**Contexto:** pedido .NET 10 LTS; a máquina só tem SDK/runtime 11.
**Decisão:** compilar para `net10.0`; em desenvolvimento rodar no runtime 11; release self-contained. (Escolha do dono.)

## ADR-003 — `nuget.config` e `global.json` locais (2026-11-15)

**Contexto:** `NuGet.Config` global sem fontes; `dotnet test` do SDK 11 exige Microsoft.Testing.Platform.
**Decisão:** configuração local em `suite/`, sem alterar a configuração global do usuário.

## ADR-004 — SQLite + EF Core com migrações aditivas (2026-11-15)

`DateTimeOffset` convertido para binário (ordenável); FKs sem cascata; índices únicos por nome normalizado.

## ADR-005 — Catálogo de navegação no Core (2026-11-15)

O menu completo da seção 6.1 vive em `NavigationCatalog` com fase e situação. Itens não implementados abrem
uma tela de status. Testado para não declarar como pronto o que não está.

## ADR-006 — Captura de pacotes própria (Fase 2) em vez do REA no Windows (2026-11-15)

**Contexto:** `rea inspect-packet-capture` exige Linux + `prlimit` (retorna `capability_unavailable` no Windows).
**Decisão:** Network Capture/Packet Inspector usarão SharpPcap + PacketDotNet no processo .NET, com TShark como
validação cruzada.

## ADR-007 — Auditoria legível no mesmo commit da alteração (2026-11-15)

Cada serviço registra `ActivityEntry` antes do `SaveChanges`; entidade e auditoria são gravadas na mesma transação.

## ADR-008 — Leitor/gravador PCAPNG próprios (2026-11-15)

**Contexto:** SharpPcap/libpcap gravam só pcap clássico; o prompt exige PCAPNG e leitura paginada de arquivos grandes.
**Decisão:** `PcapNgWriter` (SHB+IDB+EPB, microssegundos) e `CaptureFileReader` (PCAPNG + pcap µs/ns, LE/BE) próprios, com acesso por
deslocamento e varredura de cabeçalhos para paginação. Validados contra o TShark.
**Consequência:** índice em memória de 8 bytes por pacote; arquivo nunca carregado inteiro.

## ADR-009 — Bibliotecas de rede (2026-11-15)

SharpPcap 6.3.1 (MIT) para captura; PacketDotNet 1.4.8 (MPL-2.0, usada sem modificação via NuGet) para decodificação.

## ADR-010 — Captura como evidência (2026-11-15)

Capturas não têm exclusão; bloqueiam exclusão de ambiente/projeto; importação copia e confere SHA-256 sem tocar no original;
captura ativa durante uma queda é recuperada como "Interrompida" na próxima abertura.

## ADR-011 — Bot Lab AI nas Fases 4 e 7 (2026-11-15)

Manual Gameplay e Session Recorder na Fase 4 (dependem da captura da Fase 2 e da timeline da Fase 4); a parte de IA (Python via
Named Pipes) na Fase 7. AI Gameplay só executa ações em ambientes Laboratório local/Emulador com aprovação humana.
