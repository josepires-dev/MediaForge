#requires -Version 5.1
<#
.SYNOPSIS
  Gera o pacote MSIX (e a pasta publicada) do MediaForge para win-x64.
.PARAMETER Sign
  Assina o pacote com o certificado de desenvolvimento criado por new-dev-cert.ps1.
#>
[CmdletBinding()]
param(
  [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  $out = Join-Path $root 'artifacts\publish'
  if (Test-Path $out) { Remove-Item $out -Recurse -Force }

  # O valor tem de ser calculado antes do array: dentro de @(...) a virgula tem
  # precedencia sobre o '+', o que partia o argumento em dois e fazia o dotnet
  # receber "mais do que um projeto" (MSB1008). A barra final e '/' para nao
  # escapar a aspa quando o caminho contem espacos.
  $pkgDir = ((Join-Path $root 'AppPackages') -replace '\\', '/') + '/'
  $pkgDirArg = "-p:AppxPackageDir=$pkgDir"

  $props = @(
    '-c', 'Release',
    '-p:Platform=x64',
    '-p:RuntimeIdentifier=win-x64',
    '-p:GenerateAppxPackageOnBuild=true',
    $pkgDirArg
  )

  if ($Sign) {
    $pfx = Join-Path $root 'artifacts\MediaForge-dev.pfx'
    if (-not (Test-Path $pfx)) { throw 'Certificado não encontrado. Execute scripts\new-dev-cert.ps1 primeiro.' }
    $props += '-p:AppxPackageSigningEnabled=true'
    $props += "-p:PackageCertificateKeyFile=$pfx"
  }

  dotnet publish .\MediaForge.csproj @props -o $out
  if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }

  Write-Host "Publicado em: $out"
  Write-Host "Pacotes MSIX em: $(Join-Path $root 'AppPackages')"
}
finally {
  Pop-Location
}
