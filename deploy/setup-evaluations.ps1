param(
    [Parameter(Mandatory, Position = 0)][string]$Environment,
    [string]$Repository = 'NencyBatada/Kairo',
    [int]$BootstrapPullRequest = 0
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
$repo = (Invoke-Native -FilePath 'gh' -ArgumentList @('api', "repos/$Repository") -Capture) | ConvertFrom-Json
if ($BootstrapPullRequest) {
    $pr = (Invoke-Native -FilePath 'gh' -ArgumentList @('api', "repos/$Repository/pulls/$BootstrapPullRequest") -Capture) | ConvertFrom-Json
    if ($pr.state -ne 'open' -or $pr.head.repo.full_name -ne $Repository -or $pr.head.ref -ne 'feature/ai-quality-tests' -or $pr.base.ref -ne 'develop') {
        throw 'Bootstrap requires the open same-repository AI quality implementation PR against develop.'
    }
}
$settings = Initialize-KairoEvaluations -Environment (Get-KairoEnvironment -Name $Environment) -Repository $Repository -RepositoryId $repo.id -OwnerId $repo.owner.id -BootstrapPullRequest $BootstrapPullRequest
foreach ($item in $settings.GetEnumerator()) {
    Invoke-Native -FilePath 'gh' -ArgumentList @('variable', 'set', $item.Key, '--repo', $Repository, '--body', $item.Value)
}
Write-Host 'Evaluation identity and repository variables are configured.'
if ($BootstrapPullRequest) {
    Write-Host 'After this PR merges, rerun without BootstrapPullRequest to remove the one-PR bootstrap trust.'
}
