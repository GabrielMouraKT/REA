# Relatório — Fase 3 (Protocolo e binários)

Data: 15/11/2026 · Branch `feature/rea-advanced-suite` (local, sem push) · Versão 0.3.0

## 1. O que foi entregue

| Módulo                      | O que faz                                                                                                                                                                                                                                       |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Evidence › Files**        | Importa arquivos para o projeto: cópia com SHA-256 conferido (original intocado), idempotente por projeto, formato pelos bytes, arquitetura e resumo; detalhes, exportação, mostrar na pasta                                                    |
| **Binary Analysis**         | PE (PEReader + imports/exports lidos por RVA com limites), ELF (cabeçalho e seções), hashes, entropia por arquivo e por seção, versão do produto, assinatura embutida (presença, sem validar), strings ASCII/UTF-16 em fluxo, hex por faixa     |
| **File Analysis**           | Inventário de pasta (sem seguir links/junções, itens ignorados com motivo), repetidos, manifesto JSON, comparação com detecção de movidos, relatório Markdown, cancelamento                                                                     |
| **Ghidra Integration**      | `analyzeHeadless` em projeto temporário apagado ao final, script `ReaExport.java` (funções, externos, strings), log ao vivo, cancelamento que encerra a árvore de processos, resultado guardado e auditado, recuperação de análise interrompida |
| **Protocol Explorer**       | Hipóteses de enquadramento por prefixo de tamanho (offset 0/2/4, 2/4 bytes, LE/BE, inclui ou não o cabeçalho) com confiança, opcodes **candidatos**, corpo testado como Protobuf, aviso de lacunas                                              |
| **Protobuf Analyzer**       | Decoder wire-format só com números de campo e interpretações; parser `.proto` (message, enum, oneof, map, nested, reserved) com rótulos sem inventar nomes; montador de mensagens de teste                                                      |
| **HTTP/HTTPS Analyzer**     | HAR e captura via TShark; Authorization/Cookie/Set-Cookie sempre ocultados; corpos não copiados; TLS listado por SNI como cifrado                                                                                                               |
| **Kali Tools**              | WSL e distribuições; versão de uma lista fixa de ferramentas; nada de comando livre nem instalação                                                                                                                                              |
| **Settings › Integrations** | Pasta do Ghidra validada e salva; ferramentas encontradas                                                                                                                                                                                       |
| **Modo Simples × Técnico**  | Seletor global salvo; telas novas mostram resumo em linguagem simples e escondem tabelas/hex no modo simples                                                                                                                                    |

## 2. Como foi feito

- Projetos novos: `REA.BinaryAnalysis`, `REA.ProtocolAnalysis`, `REA.ToolIntegration` (com `ProcessRunner`: argumentos em lista, log ao vivo, timeout e cancelamento da árvore de processos).
- `HttpAnalyzer` no `REA.PacketAnalyzer`, reaproveitando o localizador/executor do TShark da Fase 2.
- Banco: migração aditiva `AddAnalyzedFiles` (tabela `analyzed_files`, FKs sem cascata, índices por projeto/data, projeto+SHA-256, ambiente, situação do Ghidra). Configuração da pasta do Ghidra em `app_settings`.
- Núcleo sem dependência de ferramentas: `IBinaryInspector` e `IGhidraAnalyzer` são contratos do Core implementados pelos módulos.

## 3. Verificação

**Testes automáticos — 122/122, 0 ignorados** (build com 0 avisos):

| Projeto                                                                                                   | Resultado |
| --------------------------------------------------------------------------------------------------------- | --------- |
| REA.UnitTests                                                                                             | 20/20     |
| REA.NetworkTests (inclui HTTP com TShark real e HAR)                                                      | 28/28     |
| REA.IntegrationTests (inclui arquivos-evidência e Ghidra real pelo serviço)                               | 27/27     |
| REA.BinaryTests (notepad.exe, kernel32.dll e assembly .NET reais; ELF/PAK sintéticos; junção não seguida) | 16/16     |
| REA.ProtocolTests                                                                                         | 22/22     |
| REA.ToolTests (Ghidra 12.1.4 real, cancelamento e limpeza, timeout matando a árvore)                      | 9/9       |

**Interface real** (UI Automation, pasta de dados temporária, capturas só da janela do REA): criar projeto → importar
`choice.exe` → Binary Analysis nos dois modos (Seções, Strings, Hex) → Ghidra pela tela (94 funções, 80 externos,
129 strings) → importar captura de laboratório → Protocol Explorer (hipótese correta: tamanho u16 LE incluindo cabeçalho,
confiança Alta, 20 mensagens, opcodes 0x0205 ×13 e 0x0101 ×7, corpo 100% Protobuf) → Protobuf Analyzer → HTTP Analyzer
(GET 200, cookie ocultado) → File Analysis → Kali Tools → Integrations.

## 4. Problemas encontrados e corrigidos

| Problema                                                                                  | Correção                                                                   |
| ----------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Listas grandes (100 mil strings/funções) adicionadas uma a uma travariam a tela           | Listas substituídas de uma vez; hash e strings calculados em segundo plano |
| Visão geral técnica aparecia no modo simples                                              | Quadro de detalhes só no modo técnico                                      |
| Teste de cancelamento do Ghidra falhava quando outro teste rodava o Ghidra ao mesmo tempo | Pasta de trabalho própria por execução (`workRoot`)                        |
| Texto "Neste captura"                                                                     | "Nesta captura" / "Neste arquivo HAR"                                      |
| Aba Diferenças vazia sem explicação                                                       | Texto orientando a comparar                                                |
| Colunas cortadas (Ghidra, Codificação, tamanhos)                                          | Larguras ajustadas                                                         |

## 5. Arquivos (todos em `suite/`)

| Assunto      | Arquivos                                                                                                                                            |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Binários     | `src/REA.BinaryAnalysis/{FileIdentifier,PeAnalyzer,ElfAnalyzer,StringExtractor,DirectoryInventory,BinaryInspector}.cs`                              |
| Protocolo    | `src/REA.ProtocolAnalysis/{ProtobufWire,ProtoSchema,FramingDetector}.cs`, `src/REA.PacketAnalyzer/HttpAnalyzer.cs`, `HexDump` com deslocamento base |
| Ferramentas  | `src/REA.ToolIntegration/{ProcessRunner,GhidraHeadless,GhidraAnalyzerAdapter,KaliCatalog}.cs`                                                       |
| Domínio/Core | `REA.Domain/AnalyzedFiles.cs`, `REA.Core/{FileEvidence,Settings,Navigation,Projects,Environments}.cs`                                               |
| Infra        | `REA.Infrastructure/{AnalyzedFileRepository,SettingsRepository,ReaDbContext,ReaPaths}.cs`, migração `AddAnalyzedFiles`                              |
| Interface    | `ViewModels/{AnalysisViewModels,ProtocolViewModels}.cs`, `Views/AnalysisPages.xaml`, `ViewModeService`, seletor de modo na janela                   |
| Testes       | `tests/REA.{BinaryTests,ProtocolTests,ToolTests}`, `IntegrationTests/FileEvidenceTests.cs`, `NetworkTests/HttpAnalyzerTests.cs`                     |
| Docs         | `docs/guia/` (seções da Fase 3 e imagens), `docs/GENERAL/USER_GUIDE.md` §11, este relatório, `REQUISITOS/04` e `05`                                 |

## 6. Pendências e limites conhecidos

- SAZ (Fiddler) não suportado; HTTP só por HAR ou TShark. HTTPS só é legível com chaves próprias (não implementado).
- Recursos de PE (ícones, manifestos) e xrefs/decompilação do Ghidra não são exportados ainda.
- Protocol Explorer assume que a direção começa no início de uma mensagem; capturas iniciadas no meio da conexão podem não encaixar.
- Kali: nenhuma distribuição WSL instalada nesta máquina; a verificação de ferramentas foi testada só no caminho "distribuição ausente".
- Próximo: Git Control Center (modo manual, login do `gh`) e depois Claude Control Center (Claude Code instalado) — ver `IMPLEMENTATION_PLAN.md`.
