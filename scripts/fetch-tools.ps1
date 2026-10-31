#requires -Version 5.1
<#
.SYNOPSIS
  Descarrega e verifica as ferramentas grandes (FFmpeg, Pandoc) que não estão no repositório.
.DESCRIPTION
  Lê scripts/tools.lock.json, descarrega cada arquivo (se ainda não estiver em cache),
  confirma o SHA-256 do arquivo, extrai apenas o executável necessário para Tools/
  e confirma o SHA-256 do executável. Não faz nada se as ferramentas já estiverem corretas.
.PARAMETER ToolsDirectory
  Destino das ferramentas (predefinição: Tools/ na raiz do projeto).
.PARAMETER CacheDirectory
  Pasta de cache dos arquivos descarregados.
.PARAMETER Force
  Volta a extrair mesmo que o executável já exista e esteja correto.
#>
[CmdletBinding()]
param(
  [string]$ToolsDirectory,
  [string]$CacheDirectory,
  [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $root 'Tools' }
if (-not $CacheDirectory) {
  $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
  $CacheDirectory = Join-Path $base 'mediaforge-tools-cache'
}
New-Item -ItemType Directory -Force -Path $ToolsDirectory, $CacheDirectory | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Sha256([string]$Path) {
  (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$lock = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'tools.lock.json') | ConvertFrom-Json
foreach ($tool in $lock.tools) {
  $target = Join-Path $ToolsDirectory $tool.target
  if (-not $Force -and (Test-Path $target) -and ((Get-Sha256 $target) -eq $tool.sha256)) {
    Write-Host "[OK]   $($tool.name) $($tool.version) já presente e verificado."
    continue
  }

  $archive = Join-Path $CacheDirectory ("{0}-{1}.zip" -f $tool.name, $tool.version)
  if (-not ((Test-Path $archive) -and ((Get-Sha256 $archive) -eq $tool.archiveSha256))) {
    Write-Host "[...]  A descarregar $($tool.name) $($tool.version)..."
    $partial = "$archive.part"
    & curl.exe --fail --silent --show-error --location --retry 3 --retry-delay 2 --connect-timeout 20 --output $partial $tool.url
    if ($LASTEXITCODE -ne 0) { throw "Falha ao descarregar $($tool.url) (curl $LASTEXITCODE)." }
    $actual = Get-Sha256 $partial
    if ($actual -ne $tool.archiveSha256) {
      Remove-Item -Force $partial
      throw "SHA-256 do arquivo $($tool.name) não corresponde. Esperado $($tool.archiveSha256), obtido $actual."
    }
    Move-Item -Force $partial $archive
  }

  $temporary = "$target.part"
  $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
  try {
    $entry = $zip.GetEntry($tool.archiveEntry)
    if (-not $entry) { throw "Entrada '$($tool.archiveEntry)' não existe no arquivo de $($tool.name)." }
    $source = $entry.Open()
    try {
      $sink = [System.IO.File]::Create($temporary)
      try { $source.CopyTo($sink) } finally { $sink.Dispose() }
    } finally { $source.Dispose() }
  } finally { $zip.Dispose() }

  $actualExe = Get-Sha256 $temporary
  if ($actualExe -ne $tool.sha256) {
    Remove-Item -Force $temporary
    throw "SHA-256 do executável $($tool.target) não corresponde. Esperado $($tool.sha256), obtido $actualExe."
  }
  Move-Item -Force $temporary $target
  Write-Host "[OK]   $($tool.name) $($tool.version) instalado em $target."
}
