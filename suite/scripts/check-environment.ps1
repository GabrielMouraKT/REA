<#
.SYNOPSIS
  Verifica os pré-requisitos do REA Advanced Suite. Não instala nada.
.OUTPUTS
  Código de saída 0 se os obrigatórios estiverem presentes; 1 caso contrário.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$missingRequired = 0

function Report([string]$name, [bool]$ok, [string]$detail, [bool]$required, [string]$howTo) {
    $status = if ($ok) { 'OK      ' } elseif ($required) { 'FALTANDO' } else { 'opcional' }
    Write-Host ("[{0}] {1,-22} {2}" -f $status, $name, $detail)
    if (-not $ok) {
        Write-Host ("           -> {0}" -f $howTo)
        if ($required) { $script:missingRequired++ }
    }
}

function Try-Run([string]$exe, [string[]]$arguments) {
    try { (& $exe @arguments 2>&1 | Select-Object -First 1) -join '' } catch { $null }
}

$sdks = Try-Run 'dotnet' @('--list-sdks')
Report '.NET SDK (10+)' ([bool]$sdks) "$sdks" $true 'Instale o .NET SDK 10 ou superior: https://dotnet.microsoft.com/'

$node = Try-Run 'node' @('--version')
Report 'Node.js (22.19+/24.11+)' ([bool]$node) "$node" $true 'Instale o Node.js LTS: https://nodejs.org/'

$npcap = Join-Path $env:SystemRoot 'System32\Npcap\Packet.dll'
$npcapVer = if (Test-Path $npcap) { (Get-Item $npcap).VersionInfo.ProductVersion } else { '' }
Report 'Npcap' (Test-Path $npcap) $npcapVer $false 'Instalador de https://npcap.com com "loopback support"'

$tshark = (Get-Command tshark -ErrorAction SilentlyContinue).Source
if (-not $tshark -and (Test-Path "$env:ProgramFiles\Wireshark\tshark.exe")) { $tshark = "$env:ProgramFiles\Wireshark\tshark.exe" }
Report 'TShark (Wireshark)' ([bool]$tshark) "$tshark" $false 'winget install WiresharkFoundation.Wireshark'

$ghidra = if ($env:GHIDRA_INSTALL_DIR) { Join-Path $env:GHIDRA_INSTALL_DIR 'support\analyzeHeadless.bat' } else { '' }
Report 'Ghidra' ($ghidra -and (Test-Path $ghidra)) "$env:GHIDRA_INSTALL_DIR" $false 'Baixe o Ghidra e defina GHIDRA_INSTALL_DIR'

$java = (Get-Command java -ErrorAction SilentlyContinue).Source
Report 'Java (JDK 21)' ([bool]$java) "$java" $false 'Instale um JDK 21 (ex.: Temurin)'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$reaScript = Join-Path $repoRoot 'scripts\rea.mjs'
$reaBuilt = Test-Path (Join-Path $repoRoot 'dist')
Report 'REA CLI compilado' ((Test-Path $reaScript) -and $reaBuilt) "$reaScript" $false "Na raiz do repositório: npm ci; npm run build:cached"

$wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
$distros = if (Test-Path $wsl) { (& $wsl -l -q 2>$null) -replace "`0", '' | Where-Object { $_.Trim() } } else { @() }
Report 'WSL / Kali' ([bool]$distros) ($distros -join ', ') $false 'wsl --install -d kali-linux'

$wix = (Get-Command wix -ErrorAction SilentlyContinue).Source
Report 'WiX Toolset' ([bool]$wix) "$wix" $false 'dotnet tool install --global wix'

Write-Host ''
if ($missingRequired -gt 0) { Write-Host "$missingRequired pré-requisito(s) obrigatório(s) ausente(s)."; exit 1 }
Write-Host 'Pré-requisitos obrigatórios presentes.'
exit 0
