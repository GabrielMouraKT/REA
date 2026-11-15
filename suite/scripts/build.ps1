<#
.SYNOPSIS  Compila a solução REA (avisos tratados como erro).
.PARAMETER Configuration  Debug (padrão) ou Release.
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$suite = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $suite
try {
    dotnet tool restore | Out-Host
    dotnet build REA.sln -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host 'Falha na compilação.'; exit $LASTEXITCODE }
    Write-Host "Compilação $Configuration concluída."
}
finally { Pop-Location }
