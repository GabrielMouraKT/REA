<#
.SYNOPSIS  Abre o aplicativo desktop compilado (Debug por padrão).
.PARAMETER DataRoot  Pasta de dados alternativa (define REA_HOME só para esta execução).
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [string]$DataRoot)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot "..\src\REA.Desktop\bin\$Configuration\net10.0-windows\REA.Desktop.exe"
if (-not (Test-Path $exe)) { Write-Host "Executável não encontrado. Rode .\scripts\build.ps1 -Configuration $Configuration"; exit 1 }
if ($DataRoot) { $env:REA_HOME = $DataRoot }
Start-Process -FilePath (Resolve-Path $exe)
