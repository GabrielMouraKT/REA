# Instalação — REA Advanced Suite

## 1. Usuário final (pacote pronto)

1. Copie a pasta `REA.Desktop-win-x64` (gerada por `scripts\publish.ps1`) para o computador.
   Ela é _self-contained_: **não precisa do .NET instalado**.
2. Execute `REA.Desktop.exe`. A primeira abertura cria `%LOCALAPPDATA%\REA` e o banco (leva alguns segundos).
3. Para capturar tráfego, instale o **Npcap** (npcap.com) marcando _Support loopback traffic capture_ e
   _Install Npcap in WinPcap API-compatible Mode_. Para abrir capturas, instale o Wireshark
   (`winget install WiresharkFoundation.Wireshark`).

Instalador MSI: planejado (requer WiX Toolset; ver `KNOWN_ISSUES.md`).

## 2. Desenvolvedor

```powershell
git clone https://github.com/GabrielMouraKT/REA.git
cd REA
git switch feature/rea-advanced-suite

# motor REA (TypeScript)
npm ci
npm run build:cached

# suíte (.NET)
cd suite
.\scripts\check-environment.ps1   # lista o que falta; não instala nada
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

Observações do ambiente:

- **.NET**: a suíte mira `net10.0`. Com apenas o SDK/runtime 11 instalado, o `RollForward=LatestMajor`
  (em `Directory.Build.props`) faz rodar no 11. A publicação de release é self-contained.
- **NuGet**: `suite\nuget.config` define nuget.org porque o `NuGet.Config` global deste computador está sem fontes.
- **Testes**: `suite\global.json` ativa o Microsoft.Testing.Platform (exigido pelo `dotnet test` no SDK 10+).
  Use `dotnet test --solution REA.sln`.
- **Migrações EF**: ferramenta local — `dotnet tool restore` e depois
  `dotnet ef migrations add <Nome> --project src/REA.Infrastructure --output-dir Migrations`.
  Se só houver runtime 11, defina `$env:DOTNET_ROLL_FORWARD='Major'` antes.

## 3. Ferramentas externas

| Ferramenta            | Instalação                                                                       | Usada por                     |
| --------------------- | -------------------------------------------------------------------------------- | ----------------------------- |
| Npcap 1.90+           | npcap.com (instalador gráfico; a edição gratuita não instala em modo silencioso) | Captura (Fase 2), Wireshark   |
| Wireshark/TShark 4.6+ | `winget install WiresharkFoundation.Wireshark`                                   | Validação de PCAP/PCAPNG      |
| Ghidra 12 + JDK 21    | Zip do Ghidra + `GHIDRA_INSTALL_DIR`; JDK Temurin 21                             | Motor REA, Ghidra Integration |
| WSL2 + Kali           | `wsl --install -d kali-linux`                                                    | Kali Tools (opcional)         |
| WiX Toolset           | `dotnet tool install --global wix`                                               | Instalador MSI                |
