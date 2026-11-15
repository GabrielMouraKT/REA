# REA Advanced Suite — Relatório de auditoria (Fase 0)

Data: 2026-11-15 · Branch: `feature/rea-advanced-suite` (criada a partir de `main` @ `653067d7`) · Nenhum push feito.

## 1. Repositório

| Item     | Estado                                                                                                                 |
| -------- | ---------------------------------------------------------------------------------------------------------------------- |
| Origem   | `https://github.com/GabrielMouraKT/REA.git` (fork de `morluto/rea`, licença MIT)                                       |
| Pasta    | `Desktop/REA ADVANCED SUITE` — clone com `.git` próprio (antes a pasta caía no repo da home `GabrielMouraKT/teste`)    |
| Pacote   | `rea-agents` v6.4.0, binários `rea` / `rea-agents`                                                                     |
| Stack    | TypeScript, Node (`.nvmrc` 24.18.0, `npm@11.16.0`), Vitest, Turbo, oxlint                                              |
| Natureza | Servidor MCP + CLI de engenharia reversa **somente local**. **Não** é aplicação desktop e **não** tem nada em .NET/WPF |

### Módulos existentes que a suíte pode reaproveitar (via CLI/MCP, sem reescrever)

| Módulo REA (`src/`)                         | Uso na suíte                                                                     |
| ------------------------------------------- | -------------------------------------------------------------------------------- |
| `ghidra/` (62 arquivos)                     | Análise headless, funções, strings, xrefs, decompilação → **Ghidra Integration** |
| `captures/TsharkCaptureProvider.ts`         | Inspeção de PCAP/PCAPNG via TShark → **Packet Inspector** (depende de TShark)    |
| `native/`, `windows/`                       | PE, seções, imports, recursos → **Binary Analysis**                              |
| `artifacts/`, `firmware/`                   | Identificação de formato, extração limitada → **File Analysis**                  |
| `dotnet/`                                   | Inspeção de assemblies .NET                                                      |
| `sqlite/`                                   | Inspeção de bancos SQLite                                                        |
| `frida/`, `adb/`, `browser/`, `javascript/` | Fora do escopo inicial; ficam disponíveis                                        |

Regras do `AGENTS.md` do REA que valem para a suíte: Ghidra só com banco efêmero, sem alterar bytes; instaladores não instalam software não relacionado; separar observação, derivação, inferência e desconhecido.

## 2. Ambiente Windows

| Ferramenta             | Estado                                                                    | Impacto                                                                     |
| ---------------------- | ------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| Windows 11 Home 26200  | OK                                                                        | —                                                                           |
| .NET SDK               | **11.0.100** (sem SDK 10)                                                 | Compila `net10.0`; decidir se o alvo é `net10.0` (LTS, pedido) ou `net11.0` |
| Node / npm             | 24.14.1 / 11.19.1                                                         | `.nvmrc` pede 24.18.0; dentro do `engines` (^24.11)                         |
| Git                    | 2.53                                                                      | OK                                                                          |
| Java                   | Temurin 21.0.13                                                           | OK para Ghidra                                                              |
| Ghidra                 | 12.1.4 em `C:\Tools\ghidra_12.1.4_PUBLIC` (`GHIDRA_INSTALL_DIR` definido) | Integração real possível                                                    |
| **Npcap**              | **Ausente**                                                               | **Bloqueia a captura real (critério obrigatório da Fase 2)**                |
| **Wireshark / TShark** | **Ausente**                                                               | Bloqueia o Packet Inspector do REA e a validação cruzada                    |
| WSL / Kali             | Ausente                                                                   | Kali Integration fica só como detecção + documentação                       |
| WiX Toolset            | Ausente                                                                   | Instalador MSI pendente (`dotnet tool install wix` resolve)                 |
| Plugin superpowers     | **Ausente**                                                               | Regra global do dono: deve rodar antes de projeto novo                      |

## 3. Decisões e riscos

1. **Onde fica a solução .NET.** `src/` já pertence ao REA em TypeScript. Proposta: `suite/REA.sln` com `suite/src/REA.*` e `suite/tests/*`, sem tocar no código TS. O desktop chama o REA pelo CLI/MCP (`scripts/rea.mjs`) em vez de portar código.
2. **Escopo.** O prompt descreve cinco plataformas e mais de 20 projetos. É trabalho de muitas sessões. As fases 0→9 com `DEVELOPMENT_STATUS.md` são o mecanismo de continuidade.
3. **Captura real** precisa do Npcap instalado (instalador do npcap.com, com "WinPcap API-compatible mode" e "loopback support"). Sem ele, a Fase 2 não pode ser declarada concluída.
4. **MIR4.** Template de pesquisa e análise passiva de arquivos e tráfego próprios; sem bypass do token do launcher, sem anti-cheat, sem spoofing (seção 15 do prompt).

## 4. Estado da instalação do REA

- `npm ci`: OK (avisos: 7 vulnerabilidades npm, 5 moderadas e 2 altas, em dependências; `esbuild` postinstall não liberado por `allowScripts`).
- `npm run build:cached`: OK, 3/3 tarefas (compile + skills) em ~1 min.
- `node scripts/rea.mjs --version` → `6.4.0`.

## 5. Ferramentas instaladas durante a Fase 0 (autorizadas pelo dono)

| Ferramenta             | Como                                                                                                                                                     | Verificação                                                       |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| Wireshark/TShark 4.6.9 | `winget install WiresharkFoundation.Wireshark` (hash do instalador verificado pelo winget)                                                               | `tshark --version`                                                |
| Npcap 1.90             | Instalador oficial de npcap.com, assinatura Authenticode válida (Nmap Software LLC), SHA-256 `682B57E1…1199B6`; instalação manual com suporte a loopback | Serviço `npcap` Running; `tshark -D` lista `\Device\NPF_Loopback` |

### Teste real de captura (pré-Fase 2)

Captura de 6 s em `NPF_Loopback` com filtro `tcp port 47123`, tráfego gerado por cliente/servidor Node local → `smoke.pcapng` → reaberto com `tshark -r`:

- 1 conversa TCP `127.0.0.1:60571 <-> 127.0.0.1:47123`, 11 frames, 540 bytes.
- `follow,tcp,ascii` reconstruiu `ping-from-client` e `pong-from-server`.

Conclusão: o ambiente suporta captura real. O critério da Fase 2 ainda precisa ser cumprido **pela própria suíte** (SharpPcap), não só pelo TShark.

## 6. Decisões do dono (2026-11-15)

- Superpowers: pular desta vez.
- Solução .NET em `suite/` dentro deste repo.
- Alvo `net10.0` LTS.
- Npcap + Wireshark: instalados pelo agente com autorização.
