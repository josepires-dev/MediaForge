#requires -Version 5.1
<#
.SYNOPSIS
  Instala em modo de desenvolvimento o pacote gerado (requer Modo de Programador ativo).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pkgDir = Join-Path $root 'AppPackages'

$manifest = Get-ChildItem -Path (Join-Path $root 'bin'), $pkgDir -Recurse -Filter 'AppxManifest.xml' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($manifest) {
  Write-Host "A registar manifesto de desenvolvimento: $($manifest.FullName)"
  Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown
  return
}

$msix = Get-ChildItem -Path $pkgDir -Recurse -Filter 'MediaForge*.msix' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msix) {
  $msix = Get-ChildItem -Path $pkgDir -Recurse -Include '*.msix', '*.appx' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch 'Dependencies\\(arm|arm64|x86)' } | Select-Object -First 1
}
if (-not $msix) { throw 'Nenhum pacote encontrado. Execute scripts\publish.ps1 primeiro.' }
Write-Host "A instalar pacote: $($msix.FullName)"
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
