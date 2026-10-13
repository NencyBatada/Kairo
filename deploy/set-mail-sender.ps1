param([Parameter(Mandatory, Position = 0)][string]$Environment)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
Set-KairoMailSender -Environment (Get-KairoEnvironment -Name $Environment)
Write-Host "The notification sender mailbox is signed in for $Environment. Run deploy.bat $Environment to start sending email."
