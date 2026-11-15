<#
.SYNOPSIS  Gera uma publicação self-contained (não exige .NET instalado) em suite\publish\REA.Desktop-win-x64.
#>
[CmdletBinding()]
param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$suite = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $suite "publish\REA.Desktop-$Runtime"
Push-Location $suite
try {
    dotnet publish src\REA.Desktop\REA.Desktop.csproj -c Release -r $Runtime --self-contained true -o $out | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host 'Falha na publicação.'; exit $LASTEXITCODE }
    $exe = Join-Path $out 'REA.Desktop.exe'
    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash
    Write-Host "Publicado em $out"
    Write-Host "SHA-256 REA.Desktop.exe: $hash"
}
finally { Pop-Location }
