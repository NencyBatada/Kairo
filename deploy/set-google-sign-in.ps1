param([Parameter(Mandatory, Position = 0)][string]$Environment)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
$target = Get-KairoEnvironment -Name $Environment
$clientId = Read-Host 'OAuth client ID'
$secret = Read-Host 'OAuth client secret' -AsSecureString
$clientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret))
Set-KairoGoogleSignIn -Environment $target -ClientId $clientId -ClientSecret $clientSecret
Write-Host "Google sign-in is stored for $Environment. Run deploy.bat $Environment to turn it on."
