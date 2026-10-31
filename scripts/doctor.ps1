#requires -Version 5.1
<#
.SYNOPSIS
  Verifica o ambiente do MediaForge (ferramentas portáteis, política do ImageMagick e pasta temporária).
.DESCRIPTION
  Executa o EnvironmentDoctor através dos smoke tests (--doctor).
  Código de saída: 0 = tudo OK, 1 = pelo menos uma verificação falhou.
.PARAMETER Configuration
  Debug ou Release (predefinição: Release).
#>
[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  dotnet run --project .\Tests\MediaForge.SmokeTests.csproj -c $Configuration -- $root --doctor
  exit $LASTEXITCODE
}
finally {
  Pop-Location
}
