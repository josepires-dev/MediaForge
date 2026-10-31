#requires -Version 5.1
<#
.SYNOPSIS
  Compila a solução MediaForge e executa os smoke tests.
.PARAMETER Configuration
  Debug ou Release (predefinição: Release).
.PARAMETER SkipTests
  Não executa os smoke tests.
#>
[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',
  [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  # FFmpeg e Pandoc não estão no repositório; descarrega-os se faltarem (idempotente).
  & "$PSScriptRoot\fetch-tools.ps1"

  dotnet restore .\MediaForge.csproj -r win-x64
  if ($LASTEXITCODE -ne 0) { throw 'Falha no restore.' }

  dotnet build .\MediaForge.csproj -c $Configuration -p:Platform=x64
  if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }

  if (-not $SkipTests) {
    dotnet run --project .\Tests\MediaForge.SmokeTests.csproj -c $Configuration -- $root
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos smoke tests.' }
  }
}
finally {
  Pop-Location
}
