#requires -Version 5.1
<#
.SYNOPSIS
  Cria um certificado autoassinado para assinar o MSIX em desenvolvimento.
  O Subject tem de coincidir com Publisher em Package.appxmanifest (CN=MediaForge).
#>
[CmdletBinding()]
param(
  [string]$Password = 'MediaForge-dev'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$cert = New-SelfSignedCertificate `
  -Type Custom `
  -Subject 'CN=MediaForge' `
  -KeyUsage DigitalSignature `
  -FriendlyName 'MediaForge Dev' `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath (Join-Path $dir 'MediaForge-dev.pfx') -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $dir 'MediaForge-dev.cer') | Out-Null

Write-Host "Certificado criado em $dir"
Write-Host 'Para instalar o MSIX assinado, importe MediaForge-dev.cer em "Pessoas fidedignas" (Local Machine) como administrador.'
