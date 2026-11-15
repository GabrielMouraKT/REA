<#
.SYNOPSIS  Executa testes unitários e de integração (SQLite real em pasta temporária).
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$suite = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $suite
try {
    dotnet test --solution REA.sln -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host 'Há testes falhando.'; exit $LASTEXITCODE }
    Write-Host 'Todos os testes passaram.'
}
finally { Pop-Location }
