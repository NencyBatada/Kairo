BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{ Project = 'kairo-test'; Name = 'development' }
}

Describe 'Evaluation identity provisioning' {
    BeforeEach {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:exists = $false
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $script:exists }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $script:calls.Add(($ArgumentList -join ' '))
            if ($ArgumentList[0] -eq 'projects' -and $ArgumentList[1] -eq 'describe') { '12345' }
        }
    }

    It 'creates only the evaluation resources with minimal provider permissions' {
        $settings = Initialize-KairoEvaluations -Environment $environment -Repository 'owner/repo' -RepositoryId '123' -OwnerId '456'
        $calls | Should -Contain 'iam service-accounts create kairo-evaluations --display-name Kairo bounded AI evaluations --project kairo-test'
        ($calls | Where-Object { $_ -like 'iam roles create*' }) | Should -BeLike '*--permissions aiplatform.endpoints.predict,aiplatform.locations.evaluateInstances,serviceusage.services.use*'
        ($calls | Where-Object { $_ -match 'secretAccessor|cloudsql|storage.object|roles/editor|roles/owner' }) | Should -BeNullOrEmpty
        $settings.KAIRO_EVAL_PROVIDER | Should -Be 'projects/12345/locations/global/workloadIdentityPools/kairo-evaluations/providers/github'
        $settings.KAIRO_EVAL_SERVICE_ACCOUNT | Should -Be 'kairo-evaluations@kairo-test.iam.gserviceaccount.com'
    }

    It 'updates the owned role and provider on repeated setup' {
        $script:exists = $true
        Initialize-KairoEvaluations -Environment $environment -Repository 'owner/repo' -RepositoryId '123' -OwnerId '456'
        ($calls | Where-Object { $_ -match 'service-accounts create|pools create|roles create|providers create-oidc' }) | Should -BeNullOrEmpty
        ($calls | Where-Object { $_ -like 'iam roles update*' }) | Should -HaveCount 1
        ($calls | Where-Object { $_ -like '*providers update-oidc*' }) | Should -HaveCount 1
    }
}

Describe 'OIDC trust conditions' {
    It 'binds numeric repository and owner IDs and protected workflow identities' {
        $condition = Get-KairoEvaluationTrust -Repository 'owner/repo' -RepositoryId '123' -OwnerId '456'
        $condition | Should -Match "assertion.repository_id=='123'"
        $condition | Should -Match "assertion.repository_owner_id=='456'"
        $condition | Should -Match 'ai-quality-live.yml@refs/heads/develop'
        $condition | Should -Match "'pull_request_target','workflow_dispatch'"
        $condition | Should -Not -Match "assertion.event_name=='pull_request'"
        $condition | Should -Not -Match 'refs/pull/'
    }

    It 'allows bootstrap only for the explicitly validated repository PR' {
        $condition = Get-KairoEvaluationTrust -Repository 'owner/repo' -RepositoryId '123' -OwnerId '456' -BootstrapPullRequest 31
        $condition | Should -Match "assertion.ref=='refs/pull/31/merge'"
        $condition | Should -Match 'ai-quality-bootstrap.yml@refs/pull/31/merge'
        $condition | Should -Not -Match 'refs/pull/\*'
    }

    It 'rejects claim injection through configuration' {
        { Get-KairoEvaluationTrust -Repository "owner/repo' || true" -RepositoryId '123' -OwnerId '456' } | Should -Throw
        { Get-KairoEvaluationTrust -Repository 'owner/repo' -RepositoryId '123 || true' -OwnerId '456' } | Should -Throw
    }
}
