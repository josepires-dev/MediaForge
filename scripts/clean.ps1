#Requires -Version 7.0
<#
.SYNOPSIS
    Limpa artefactos de build, cache e pastas temporárias do projeto MediaForge.
.DESCRIPTION
    Remove pastas bin, obj, AppPackages, BundleArtifacts, publish e caches do NuGet.
#>
param(
    [switch]$All
)

$ErrorActionPreference = 'Stop'

$foldersToRemove = @(
    'bin',
    'obj',
    'AppPackages',
    'BundleArtifacts',
    'artifacts',
    'publish'
)

Write-Host "A limpar artefactos de build do MediaForge..." -ForegroundColor Cyan

foreach ($folder in $foldersToRemove) {
    $path = Join-Path $PSScriptRoot ".." $folder
    if (Test-Path $path) {
        Write-Host "  A remover $folder..." -ForegroundColor Yellow
        Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($All) {
    Write-Host "  A limpar cache do NuGet..." -ForegroundColor Yellow
    dotnet nuget locals all --clear
}

Write-Host "Limpeza concluída com sucesso!" -ForegroundColor Green