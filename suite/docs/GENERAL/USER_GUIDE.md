# Guia do usuário — REA Advanced Suite

> Versão 0.2.0 (Fases 1 e 2 concluídas) · atualizado em 15/11/2026
>
> **Versão principal deste guia: [página HTML](../guia/index.html)** (busca, tema claro/escuro, imagens). Este arquivo é a versão em texto.
>
> Este guia ensina a usar **a ferramenta inteira**: o aplicativo desktop da suíte e o motor de
> engenharia reversa **REA** (CLI/MCP) que vem no mesmo repositório. Cada seção diz claramente o
> que **já funciona** e o que **ainda está planejado**. Nada aqui descreve uma tela que não existe.

## Sumário

1. [Visão geral: as duas partes da ferramenta](#1-visão-geral-as-duas-partes-da-ferramenta)
2. [Primeiros passos](#2-primeiros-passos)
3. [Aplicativo desktop](#3-aplicativo-desktop)
   - 3.1 Navegação e convenções da interface
   - 3.2 Dashboard
   - 3.3 Projetos (Novo Projeto / Meus Projetos)
   - 3.4 Ambientes
   - 3.5 Atividades (auditoria)
   - 3.6 Settings › Database
   - 3.7 Settings › Tools
   - 3.8 Settings › Backup
   - 3.9 Módulos planejados
4. [Motor REA (linha de comando e agente)](#4-motor-rea-linha-de-comando-e-agente)
5. [Fluxos de trabalho recomendados hoje](#5-fluxos-de-trabalho-recomendados-hoje)
6. [Onde ficam os dados](#6-onde-ficam-os-dados)
7. [Regras de uso responsável](#7-regras-de-uso-responsável)
8. [Solução de problemas](#8-solução-de-problemas)
9. [Perguntas frequentes](#9-perguntas-frequentes)

---

## 1. Visão geral: as duas partes da ferramenta

| Parte                            | O que é                                                                                                                                                                                                    | Onde está                                       | Estado                  |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------- | ----------------------- |
| **REA Advanced Suite (desktop)** | Aplicativo Windows (WPF/.NET) que organiza projetos de investigação, ambientes, auditoria, backup e, nas próximas fases, captura de rede, análise de protocolos, Launcher Studio, Portal Studio, Wiki e GM | `suite/`                                        | Fase 1 pronta           |
| **REA (motor)**                  | Ferramenta de engenharia reversa em TypeScript (`rea-agents` 6.4.0). Funciona pelo terminal (`rea …`) e como servidor MCP para agentes como o Claude Code                                                  | raiz do repositório (`scripts/rea.mjs`, `src/`) | Instalado e funcionando |

O desktop é a **central de organização** (projetos, evidências, histórico). O motor REA é o **instrumento
de análise** (binários, PE, .NET, Ghidra, apps JavaScript/Electron, APK etc.). Nas Fases 3 e 4 o desktop
passa a chamar o motor REA diretamente; até lá, use os dois lado a lado (seção 5).

---

## 2. Primeiros passos

### 2.1 Requisitos

| Item                                                               | Obrigatório?                   | Para quê                                                  |
| ------------------------------------------------------------------ | ------------------------------ | --------------------------------------------------------- |
| Windows 10/11 x64                                                  | Sim                            | —                                                         |
| .NET 10 ou 11 (runtime de desktop) — ou um pacote _self-contained_ | Sim para o desktop             | Rodar o aplicativo                                        |
| Node.js 22.19+ ou 24.11+                                           | Sim para o motor REA           | Rodar `rea`                                               |
| Npcap (com suporte a loopback)                                     | Para captura de rede           | Network Capture (Fase 2), Wireshark                       |
| Wireshark / TShark 4.x                                             | Recomendado                    | Abrir e validar PCAP/PCAPNG                               |
| Ghidra 12 + JDK 21                                                 | Para análise estática profunda | `rea` com Ghidra e Ghidra Integration (Fase 3)            |
| WSL2 + Kali                                                        | Opcional                       | Kali Tools (Fase 3); pacotes do REA que só rodam em Linux |
| WiX Toolset                                                        | Para gerar MSI                 | Build & Release (Fase 5) e instalador da suíte            |

Instalação passo a passo: [INSTALLATION.md](INSTALLATION.md).

### 2.2 Abrir o aplicativo

Em desenvolvimento (a partir da pasta `suite/`):

```powershell
.\scripts\build.ps1          # compila
.\scripts\run.ps1            # abre o aplicativo
```

Ou diretamente: `suite\src\REA.Desktop\bin\Debug\net10.0-windows\REA.Desktop.exe`.

Na primeira abertura o REA cria a pasta de dados, aplica as migrações do banco e abre o **Dashboard**.
Um backup automático é feito em segundo plano (no máximo um a cada 24 h).

### 2.3 Primeiro uso em 5 minutos

1. **Settings › Tools** — confira quais ferramentas externas foram encontradas.
2. **Projetos › Novo Projeto** — crie, por exemplo, _“MIR4 Laboratório Local”_, jogo _“MIR4”_.
3. **Projetos › Ambientes** — selecione o projeto e cadastre os ambientes que você vai estudar
   (ex.: _Lab local_, tipo _Laboratório local_, versão do cliente, pasta do cliente).
4. **Evidence › Atividades** — veja o registro de tudo o que foi feito.
5. **Settings › Backup › Criar backup agora** — guarde um ponto de restauração.

---

## 3. Aplicativo desktop

### 3.1 Navegação e convenções da interface

- **Menu lateral**: todas as áreas do produto (Dashboard, Projetos, Research, Comparative Analysis,
  Launcher Studio, Portal Studio, Wiki & Knowledge, GM & Server Admin, Evidence, Settings).
- **Etiqueta F2…F9** ao lado de um item = módulo **planejado** para aquela fase. Ao clicar, abre uma tela
  de status com a descrição e as dependências — nunca dados de exemplo.
- **Cabeçalho**: mostra a seção e o nome da tela atual.
- **Notificações** aparecem no canto inferior direito, com rótulo em texto (_Concluído, Atenção, Erro_),
  e somem sozinhas (erros ficam 10 s). Clique em **Fechar** para dispensar.
- **Confirmação** só aparece para ações destrutivas ou de alto impacto (arquivar, excluir, restaurar backup).
- **Botões travam** enquanto a operação está em andamento; não é preciso clicar duas vezes.
- **Formulários só são limpos depois de salvar com sucesso**. Se houver erro, a mensagem aparece em
  vermelho abaixo dos campos e o que você digitou continua lá.
- **Listas** têm paginação no banco, com 50, 100, 200 ou 500 itens por página (padrão 50).
- Atalho: na busca de projetos, **Enter** executa a busca.

### 3.2 Dashboard

Mostra apenas **dados reais** do banco local:

| Cartão                       | Significado                                             |
| ---------------------------- | ------------------------------------------------------- |
| Projetos ativos / arquivados | Contagem de projetos por situação                       |
| Ambientes                    | Total de ambientes em todos os projetos                 |
| Módulos implementados        | Quantos itens do menu já funcionam (ex.: 8 de 65)       |
| Ferramentas externas         | Quantas das 7 ferramentas monitoradas foram encontradas |

Abaixo: **Atividades recentes** (últimas 15 alterações) e **Status das integrações** (mesma tabela de
Settings › Tools). Botões: **Atualizar** e **+ Novo projeto**.

Indicadores de capturas, pacotes, fluxos TCP, binários, mensagens e opcodes entram no Dashboard quando
os módulos das Fases 2 a 4 existirem.

### 3.3 Projetos

Um **projeto** é uma investigação isolada (ex.: um jogo, um cliente, um servidor próprio). Ambientes,
evidências e descobertas de um projeto nunca se misturam com os de outro.

**Criar** — _Projetos › Novo Projeto_ (ou **+ Novo projeto** em qualquer lista):

| Campo             | Regras                                                                                                          |
| ----------------- | --------------------------------------------------------------------------------------------------------------- |
| Nome do projeto * | 3 a 120 caracteres; único (maiúsculas/minúsculas e espaços extras são ignorados: “Teste MIR4” = “ teste mir4 ”) |
| Jogo              | Opcional, até 120 caracteres                                                                                    |
| Descrição         | Opcional, até 4000 caracteres                                                                                   |

**Meus Projetos** — lista com busca (nome do projeto ou do jogo), filtro de situação
(_Ativos_, _Arquivados_, _Todos_) e paginação. Ações por linha:

| Ação                    | O que faz                                                                                                                                                 |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Editar**              | Abre o formulário preenchido; **Salvar alterações** grava e registra o antes/depois na auditoria                                                          |
| **Ambientes**           | Abre a tela de ambientes já filtrada para este projeto                                                                                                    |
| **Duplicar**            | Pede o novo nome e cria uma cópia **com os ambientes**. Evidências nunca são copiadas                                                                     |
| **Arquivar / Reativar** | Arquivar tira o projeto da lista de ativos sem apagar nada; projeto arquivado não recebe novos ambientes. Arquivar duas vezes não gera registro duplicado |
| **Excluir**             | Exclusão definitiva, **só para projeto sem ambientes**. Com histórico, o sistema pede para arquivar                                                       |

### 3.4 Ambientes

Um **ambiente** é um perfil do que está sendo investigado dentro de um projeto, por exemplo:
_MIR4 Global_, _Servidor privado A_, _Laboratório local_, _Emulador_.

1. Escolha o **Projeto** no seletor do topo.
2. Clique em **+ Novo ambiente** (desabilitado se o projeto estiver arquivado).
3. Preencha:

| Campo                   | Observação                                                            |
| ----------------------- | --------------------------------------------------------------------- |
| Nome *                  | Único dentro do projeto                                               |
| Tipo                    | Oficial, Servidor privado, Laboratório local, Emulador, Personalizado |
| Versão do cliente       | Ex.: build ou versão exibida pelo launcher                            |
| Diretório do cliente    | Pasta local da instalação analisada                                   |
| Descrição / Observações | Texto livre                                                           |

Não use credenciais, e-mails ou nomes de conta como nome de ambiente.

**Editar** e **Excluir** ficam em cada linha. Na Fase 1 ambientes ainda não têm evidências, então a exclusão
é definitiva (com confirmação). A partir da Fase 2, ambientes com capturas/evidências não poderão ser
excluídos, apenas arquivados junto com o projeto.

Os campos de hashes, endpoints observados, capturas, sessões e linha do tempo chegam na Fase 4
(_Environment Profiles_).

### 3.5 Atividades (Evidence › Atividades)

Registro de auditoria legível, em português, de toda alteração: **quando**, **operador** (usuário do
Windows), **ação**, **resumo** e os detalhes **Antes:** / **Depois:**. Escolha quantas linhas mostrar
(50/100/200/500) e clique em **Atualizar**. O registro não pode ser editado pela interface.

### 3.6 Settings › Database

Mostra o arquivo SQLite em uso, o tamanho, a pasta de dados e as **migrações aplicadas**. Se aparecer o aviso
de migrações pendentes, feche e abra o aplicativo — as migrações são aplicadas na inicialização e são sempre
aditivas (não apagam dados). **Abrir pasta de dados** abre o Explorer na pasta raiz.

### 3.7 Settings › Tools

Detecta de verdade as ferramentas externas (encontra o arquivo e executa a ferramenta para ler a versão;
nada é instalado automaticamente):

| Ferramenta         | Como é detectada                                                      | Como instalar se faltar                                                              |
| ------------------ | --------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| Npcap              | `System32\Npcap\Packet.dll`                                           | Instalador de npcap.com, marcando _loopback support_ e _WinPcap API-compatible mode_ |
| TShark (Wireshark) | `tshark --version` no PATH ou em `Program Files\Wireshark`            | `winget install WiresharkFoundation.Wireshark`                                       |
| Ghidra             | `GHIDRA_INSTALL_DIR\support\analyzeHeadless.bat`                      | Baixar do GitHub da NSA e definir `GHIDRA_INSTALL_DIR`                               |
| Java (JDK)         | `java -version`                                                       | JDK 21 (ex.: Temurin)                                                                |
| REA CLI            | `node scripts/rea.mjs --version` no repositório (ou `REA_CLI_SCRIPT`) | `npm ci && npm run build:cached` na raiz do repositório                              |
| WSL / Kali         | `wsl -l -q`                                                           | `wsl --install -d kali-linux`                                                        |
| WiX Toolset        | `wix --version`                                                       | `dotnet tool install --global wix`                                                   |

Clique em **Detectar novamente** depois de instalar algo.

### 3.8 Settings › Backup

- **Criar backup agora** — cópia consistente do banco (API de backup online do SQLite; pode ser feita com o
  aplicativo aberto).
- **Automático** — no máximo um a cada 24 h, na abertura do aplicativo; os 14 mais recentes são mantidos.
- **Restaurar** — pede confirmação, **cria antes um backup “Antes de restaurar”** do estado atual, valida o
  arquivo (integridade + se é mesmo um banco do REA) e só então substitui os dados. Backups corrompidos são
  recusados sem tocar no banco atual.
- **Abrir pasta** — abre a pasta `backups`.

Os arquivos têm o formato `rea-AAAAMMDD-HHMMSS-mmm-<tipo>.db` e podem ser copiados para outro disco.

### 3.9 Módulos planejados

| Área                                 | Itens                                                                                                                     | Fase |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------- | ---- |
| Research                             | HTTP/HTTPS Analyzer, Protocol Explorer, Protobuf Analyzer, Binary Analysis, File Analysis, Ghidra Integration, Kali Tools | 3    |
| Comparative Analysis                 | Environment Profiles, Client/Protocol/File Comparison, Session Timeline                                                   | 4    |
| Evidence                             | Files (3), Findings e Reports (4)                                                                                         | 3–4  |
| Bot Lab AI                           | Manual Gameplay, Recorder, History, Evidence Explorer (4); AI Gameplay, Observer, Visual Recognition, Scenarios (7)       | 4, 7 |
| Launcher Studio                      | Builder, Visual Editor, Asset Manager, Configuration, Update/Integrity Manager, Build & Release                           | 5    |
| Portal Studio                        | Website Builder, Account Portal, Download Center, News & Events, API Configuration, Deployment                            | 6    |
| Wiki & Knowledge / GM & Server Admin | Catálogos, Opcode Database, GM Command Wiki, adaptadores e console                                                        | 7    |
| MIR4 Research                        | Template com 45 categorias                                                                                                | 8    |
| Settings › Appearance                | Temas adicionais                                                                                                          | 9    |

A situação detalhada de cada item está em [`../../FEATURE_MATRIX.md`](../../FEATURE_MATRIX.md).

---

## 4. Motor REA (linha de comando e agente)

O REA analisa artefatos **localmente** e devolve resultados com **Evidence** (hash SHA-256 do alvo,
provedor usado, cobertura e limitações). Ele separa o que foi **observado** do que foi **inferido**.

### 4.1 Instalação (já feita neste computador)

Na raiz do repositório:

```powershell
npm ci
npm run build:cached
node scripts/rea.mjs --version     # 6.4.0
node scripts/rea.mjs doctor        # diagnóstico do ambiente
```

Para ter o comando `rea` no PATH: `npm link` na raiz (opcional). Nos exemplos abaixo, `rea` = `node scripts/rea.mjs`.

### 4.2 Comandos essenciais

| Objetivo                                                       | Comando                                                                    |
| -------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Ver tudo que existe                                            | `rea --help` · ajuda de um comando: `rea <comando> --help`                 |
| Diagnóstico                                                    | `rea doctor`                                                               |
| Visão geral de um app/binário                                  | `rea analyze <arquivo>`                                                    |
| Identificar um arquivo (formato, hash, próximos passos)        | `rea inspect-artifact <arquivo>`                                           |
| Layout de binário (seções, imports) sem executar               | `rea inspect-binary-layout <arquivo>`                                      |
| Recursos de PE (ícones, versão, manifestos)                    | `rea inspect-pe-resources <arquivo.exe>`                                   |
| Assinatura digital                                             | `rea inspect-signature <arquivo>`                                          |
| Assembly .NET (identidade, membros, assinaturas)               | `rea inspect-managed-artifact <dll>` · `rea inspect-managed-members <dll>` |
| Ler uma função como código                                     | `rea decompile …` · `rea function …`                                       |
| Buscar strings/funções analisadas                              | `rea search <texto>`                                                       |
| Referências a um endereço                                      | `rea xrefs …`                                                              |
| Rastrear uma funcionalidade                                    | `rea trace <literal>`                                                      |
| Extrair conteúdo de um pacote/arquivo compactado com segurança | `rea extract-artifact <arquivo>`                                           |
| Banco SQLite de um cliente                                     | `rea inspect-sqlite-database <arquivo.db>`                                 |
| App JavaScript/Electron                                        | `rea analyze-javascript-application <pasta>`                               |
| Comparar duas análises                                         | `rea compare <evidence1.json> <evidence2.json>`                            |
| Exportar/importar Evidence                                     | `rea evidence-export …` · `rea evidence-import …`                          |

Opções globais úteis: `--format json|yaml|md`, `--token-limit <n>` (resposta curta), `--filter-output <chaves>`.

Exemplo testado neste computador:

```powershell
node scripts/rea.mjs inspect-artifact C:\Windows\System32\notepad.exe --token-limit 600
# subject.name: notepad.exe · format: pe · sha256: 2d0b3948… · provider: rea-artifact-graph
```

### 4.3 Ghidra (análise estática profunda)

O REA usa o Ghidra em modo **headless**, com banco **efêmero** — os bytes do executável nunca são alterados.
Requisitos: `GHIDRA_INSTALL_DIR` apontando para a instalação (aqui: `C:\Tools\ghidra_12.1.4_PUBLIC`) e JDK 21.
`rea doctor` deve listar `ghidra: healthy`.

### 4.4 Usar com o Claude Code (MCP)

```powershell
node scripts/rea.mjs setup          # mostra o plano e pede aprovação antes de escrever qualquer arquivo
node scripts/rea.mjs mcp doctor     # verifica o registro MCP
```

Depois disso, basta pedir ao agente em linguagem natural, por exemplo: _“abra o binário X com o REA e liste as
funções que referenciam a string ‘login’”_. O agente usa as ferramentas `mcp__rea__*` (open_binary,
list_strings, procedure_pseudo_code, xrefs, inspect_pe_resources etc.) e devolve a evidência de cada conclusão.

### 4.5 Limitações conhecidas do motor no Windows

| Recurso                                    | Situação no Windows                                                                                                                                                |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `inspect-packet-capture` (PCAP via TShark) | **Não funciona**: o REA exige Linux com `prlimit` para limitar memória. Use o Wireshark/TShark direto, o REA dentro do WSL, ou o Network Capture da suíte (Fase 2) |
| Hopper                                     | Não disponível no Windows; use Ghidra                                                                                                                              |
| IDA / JEB                                  | Só se você tiver a licença instalada                                                                                                                               |

---

## 5. Fluxos de trabalho recomendados hoje

Enquanto os módulos das Fases 2 a 4 não ficam prontos, este é o fluxo completo usando o que já existe.

### 5.1 Organizar a investigação

1. Crie o **projeto** e um **ambiente** para cada cliente/versão estudado.
2. No campo _Observações_ do ambiente, anote a origem dos arquivos e a data da coleta.
3. Faça um **backup** antes de grandes mudanças.

### 5.2 Analisar o cliente (arquivos e binários)

```powershell
# identificação e hash de cada executável/DLL do cliente
node scripts/rea.mjs inspect-artifact "D:\Lab\Cliente\Game.exe" --format md > game-artifact.md
node scripts/rea.mjs inspect-pe-resources "D:\Lab\Cliente\Game.exe"
node scripts/rea.mjs inspect-signature "D:\Lab\Cliente\Game.exe"
# análise profunda com Ghidra
node scripts/rea.mjs analyze "D:\Lab\Cliente\Game.exe"
node scripts/rea.mjs search "Login"
```

Guarde os relatórios gerados junto do projeto. Trabalhe sempre em **cópia** dos arquivos originais.

### 5.3 Capturar tráfego do seu laboratório

1. Abra o Wireshark e escolha **Adapter for loopback traffic capture** (tráfego entre programas no mesmo PC)
   ou a placa de rede usada pelo laboratório.
2. Aplique um filtro de captura, ex.: `tcp port 7000`.
3. Salve como **PCAPNG**. Pelo terminal:

```powershell
& "C:\Program Files\Wireshark\tshark.exe" -D                                          # listar interfaces
& "C:\Program Files\Wireshark\tshark.exe" -i \Device\NPF_Loopback -f "tcp port 7000" -a duration:60 -w lab.pcapng
& "C:\Program Files\Wireshark\tshark.exe" -r lab.pcapng -q -z conv,tcp               # conversas TCP
& "C:\Program Files\Wireshark\tshark.exe" -r lab.pcapng -q -z follow,tcp,hex,0       # fluxo 0 em hex
```

Se o conteúdo estiver criptografado (TLS ou criptografia própria do jogo), o Wireshark mostra apenas bytes —
registre isso como **inconclusivo**, não como descoberta.

### 5.4 Registrar descobertas

Até o módulo _Findings_ (Fase 4), use a descrição/observações do ambiente e mantenha a classificação:
**Confirmado**, **Provável**, **Hipótese**, **Não identificado**, **Inconclusivo**. Nunca registre hipótese como fato.

---

## 6. Onde ficam os dados

| Item           | Caminho padrão                                                      |
| -------------- | ------------------------------------------------------------------- |
| Pasta raiz     | `%LOCALAPPDATA%\REA` (troque com a variável de ambiente `REA_HOME`) |
| Banco          | `…\data\rea.db`                                                     |
| Backups        | `…\backups\`                                                        |
| Logs (30 dias) | `…\logs\rea-AAAAMMDD.log`                                           |

Para uma instalação portátil, defina `REA_HOME` antes de abrir o aplicativo, por exemplo
`$env:REA_HOME = "E:\REA-dados"`.

---

## 7. Regras de uso responsável

O REA é ferramenta de pesquisa para **ambientes próprios, laboratórios e sistemas em que você tem
autorização**. A ferramenta não implementa — e não deve ser usada para — roubo de credenciais, coleta de tokens
de terceiros, força bruta, exploração de servidores de terceiros, evasão de anti-cheat, falsificação de
identidade de cliente, bypass de autenticação, spoofing de identificadores de hardware ou adulteração de
assinaturas/verificações de integridade de launchers. Captura e análise são **passivas**; qualquer ação
administrativa (Fase 7) exigirá adaptador autorizado, confirmação e auditoria.

---

## 8. Solução de problemas

| Sintoma                                                        | Causa provável                                                | O que fazer                                                                                     |
| -------------------------------------------------------------- | ------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| “Não foi possível abrir o banco de dados” ao iniciar           | Pasta sem permissão ou arquivo bloqueado por outro processo   | Feche outras instâncias; confira `REA_HOME`; veja o log em `logs\`                              |
| Ferramenta aparece como “Não encontrado”                       | Não instalada ou fora do PATH                                 | Instale (tabela da seção 3.7) e clique em **Detectar novamente**; reabra o app se mudou o PATH  |
| REA CLI “Não encontrado”                                       | Repositório não compilado ou app fora da pasta do repositório | `npm ci && npm run build:cached`; ou defina `REA_CLI_SCRIPT` com o caminho de `scripts\rea.mjs` |
| `tshark -D` não lista _NPF_Loopback_                           | Npcap instalado sem suporte a loopback                        | Reinstale o Npcap marcando _Support loopback traffic capture_                                   |
| `rea inspect-packet-capture` retorna `capability_unavailable`  | Limitação do REA no Windows                                   | Veja a seção 4.5                                                                                |
| “Este projeto tem ambientes cadastrados. Arquive…”             | Regra de preservação de histórico                             | Use **Arquivar**                                                                                |
| Restauração recusada: “O arquivo de backup está corrompido”    | Backup danificado                                             | Escolha outro backup; o banco atual não foi alterado                                            |
| `dotnet build`: “Não foi possível resolver …”                  | NuGet sem fonte configurada                                   | O `suite\nuget.config` já aponta para nuget.org; verifique acesso à internet                    |
| `dotnet test` falha com “VSTest target is no longer supported” | SDK 10+                                                       | Use `dotnet test --solution REA.sln` (o `global.json` já ativa o Microsoft.Testing.Platform)    |

Logs detalhados: `%LOCALAPPDATA%\REA\logs`.

---

## 9. Perguntas frequentes

**O REA já captura pacotes sozinho?** Sim, desde a Fase 2: Research › Network Capture (Npcap + SharpPcap), validado com tráfego real e comparado com o TShark.

**Posso usar o REA com outros jogos além do MIR4?** Sim. Projetos e ambientes são genéricos; o template MIR4
(Fase 8) é só um modelo de organização.

**O Dashboard mostra números de exemplo?** Não. Ele só mostra o que existe no banco local e o que foi
detectado no computador.

**Excluí um projeto por engano. Dá para recuperar?** Sim, restaurando um backup anterior em Settings › Backup
(o estado atual é salvo antes, então a restauração também pode ser desfeita).

---

## 10. Fase 2 — Rede (resumo)

Instruções completas, com imagens, na [página HTML](../guia/index.html#fase2-visao).

| Tela                           | Como usar                                                                                                                                                                                                                                   |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Research › Network Capture     | Projeto, nome, interface (Loopback para tráfego local), filtro BPF opcional (ex.: `tcp port 7000`), limites opcionais → **Iniciar captura** → **Parar**. Gera PCAPNG com SHA-256 e estatísticas.                                            |
| Evidence › Captures            | Escolha um projeto no filtro para **Importar PCAP/PCAPNG** (o original não é alterado). Selecione uma linha para **Abrir**, **Fluxos TCP**, **Detalhes**, **Validar com TShark**, **Reanalisar**, **Exportar cópia**, **Mostrar na pasta**. |
| Research › Packet Inspector    | Escolha a captura; filtre por protocolo, endereço, porta, texto ou só com dados; clique num pacote para ver camadas e hex/ASCII; **Exportar CSV**.                                                                                          |
| Research › TCP Stream Explorer | Escolha a captura e o fluxo; veja os bytes por direção, lacunas e linha do tempo; **Exportar bytes**. Estados: Completo, Sem lacunas (início/fim fora da captura), Incompleto.                                                              |

Regras: capturas são evidência (sem exclusão); ambiente/projeto com capturas não pode ser excluído; captura ativa durante uma queda volta como **Interrompida** na próxima abertura. O backup do banco não inclui a pasta `captures`: copie-a junto.

---

## 11. Fase 3 — Protocolo e binários (resumo)

Instruções completas, com imagens, na [página HTML](../guia/index.html#fase3-visao).

**Modo simples × modo técnico:** caixa _Modo técnico_ no canto superior direito. Desligada, cada tela mostra um resumo em
palavras simples; ligada, mostra tabelas, hexadecimal e detalhes. A escolha fica salva.

| Tela                           | Como usar                                                                                                                                                                                                                                                                                                    |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Evidence › Files               | Escolha o projeto no filtro → **Importar arquivo**. O REA guarda uma cópia com SHA-256 conferido (o original não muda); importar o mesmo conteúdo de novo não duplica. Selecione a linha para **Abrir em Binary Analysis**, **Analisar com Ghidra**, **Detalhes**, **Exportar cópia**, **Mostrar na pasta**. |
| Research › Binary Analysis     | Escolha o arquivo (ou **Abrir arquivo do computador**, só leitura). Resumo simples; no modo técnico: Visão geral, Seções (com entropia), Imports, Exports, Strings (filtro, tamanho mínimo, exportar) e Hex (posição decimal ou `0x…`).                                                                      |
| Research › File Analysis       | **Inventariar pasta** → repetidos e todos os arquivos; **Salvar/Abrir manifesto**; **Comparar com outra pasta/manifesto** (alterados, novos, removidos, movidos); **Exportar relatório** (Markdown).                                                                                                         |
| Research › Ghidra Integration  | Escolha o arquivo importado → **Analisar com Ghidra** (log ao vivo, **Cancelar**). Resultado: funções, externos e textos; fica guardado.                                                                                                                                                                     |
| Research › Protocol Explorer   | Captura → conexão → direção. Hipóteses de enquadramento com confiança, mensagens, opcodes candidatos, bytes e corpo Protobuf; **Abrir no Protobuf Analyzer**.                                                                                                                                                |
| Research › Protobuf Analyzer   | Cole hex → **Decodificar**. Opcional: **Carregar .proto** e escolher a mensagem para ver nomes. Modo técnico: **Montar mensagem de teste** (`número tipo valor`).                                                                                                                                            |
| Research › HTTP/HTTPS Analyzer | Escolha uma captura (precisa do TShark) ou **Abrir arquivo HAR**. Credenciais ocultadas; HTTPS listado como cifrado pelo nome do servidor.                                                                                                                                                                   |
| Research › Kali Tools          | Detecta WSL e distribuições; **Verificar ferramentas** consulta só a versão de uma lista fixa (nmap, tshark, tcpdump, mitmproxy, binwalk, radare2).                                                                                                                                                          |
| Settings › Integrations        | Pasta do Ghidra (vazio = usar `GHIDRA_INSTALL_DIR`) e onde estão Ghidra, TShark e WSL.                                                                                                                                                                                                                       |

Regras: arquivos são evidência (sem exclusão); projeto/ambiente com arquivos não pode ser excluído; análise do Ghidra
interrompida por fechamento do app volta como **Falhou** com o motivo. Os arquivos ficam em `%LOCALAPPDATA%\REA\files`
(o backup do banco não inclui essa pasta: copie-a junto).
