param(
    [Parameter(Mandatory, Position = 0)][string]$Environment,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
Invoke-KairoDeploy -Environment (Get-KairoEnvironment -Name $Environment) -Root (Split-Path $PSScriptRoot -Parent) -SkipTests:$SkipTests | Out-Null
