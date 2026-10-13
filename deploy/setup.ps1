param([Parameter(Mandatory, Position = 0)][string]$Environment)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
Initialize-KairoProject -Environment (Get-KairoEnvironment -Name $Environment)
Write-Host "$Environment is set up."
