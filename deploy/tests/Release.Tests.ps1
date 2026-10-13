BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{
        Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1'
        SqlTier = 'db-custom-1-3840'; SqlEdition = 'SQLSERVER_2022_EXPRESS'; SqlHighAvailability = $false
        MinInstances = @{ web = 0; api = 1 }
    }
    $script:root = 'C:\src\Kairo'
}

Describe 'Invoke-KairoDeploy' {
    BeforeEach {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:googleConfigured = $true
        Mock -ModuleName Kairo.Deploy Assert-KairoCleanTree { }
        Mock -ModuleName Kairo.Deploy Get-KairoCommit { 'abc123def456' }
        Mock -ModuleName Kairo.Deploy Invoke-KairoTests { $script:calls.Add('tests') }
        Mock -ModuleName Kairo.Deploy Get-KairoProjectNumber { '123' }
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $script:googleConfigured }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $line = $ArgumentList -join ' '
            $script:calls.Add($line)
            if ($line -like 'run services describe kairo-api *') { 'https://kairo-api-abc123-el.a.run.app' }
        }
    }

    It 'tests, builds, migrates, then deploys the services' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls[0] | Should -Be 'tests'
        $calls[1] | Should -BeLike 'builds submit*'
        $calls[2] | Should -BeLike 'run jobs deploy kairo-migrator*'
        $calls[3] | Should -BeLike 'run jobs execute kairo-migrator*--wait*'
        ($calls | Select-Object -Skip 4 | Where-Object { $_ -like 'run deploy *' }) -replace '^run deploy (\S+).*', '$1' |
            Should -Be @('kairo-agents', 'kairo-api', 'kairo-worker', 'kairo-web')
    }

    It 'skips the tests only when asked' {
        Invoke-KairoDeploy -Environment $environment -Root $root -SkipTests 3>$null | Out-Null

        Should -Invoke -ModuleName Kairo.Deploy Invoke-KairoTests -Times 0
    }

    It 'stops before building when a test fails' {
        Mock -ModuleName Kairo.Deploy Invoke-KairoTests { throw 'dotnet test failed' }

        { Invoke-KairoDeploy -Environment $environment -Root $root } | Should -Throw 'dotnet test failed'
        Should -Invoke -ModuleName Kairo.Deploy Invoke-Gcloud -Times 0
    }

    It 'stops before any service changes when the migration fails' {
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $line = $ArgumentList -join ' '
            $script:calls.Add($line)
            if ($line -like 'run jobs execute*') { throw 'migration failed' }
        }

        { Invoke-KairoDeploy -Environment $environment -Root $root } | Should -Throw 'migration failed'
        ($calls | Where-Object { $_ -like 'run deploy*' -or $_ -like '*add-iam-policy-binding*' }) | Should -BeNullOrEmpty
    }

    It 'stops before anything when the working tree has changes' {
        Mock -ModuleName Kairo.Deploy Assert-KairoCleanTree { throw 'Commit or stash your changes first.' }

        { Invoke-KairoDeploy -Environment $environment -Root $root } | Should -Throw 'Commit or stash*'
        $calls | Should -BeNullOrEmpty
    }

    It 'builds every image with Cloud Build, tagged with the commit' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $build = $calls | Where-Object { $_ -like 'builds submit*' }
        $build | Should -BeLike "builds submit $root --config $(Join-Path $root 'deploy/cloudbuild.yaml')*"
        $build | Should -BeLike '*--service-account projects/kairo-test/serviceAccounts/kairo-build@kairo-test.iam.gserviceaccount.com*'
        $build | Should -BeLike '*--gcs-source-staging-dir gs://kairo-test-builds/source*'
        $build | Should -BeLike '*--substitutions=_REGISTRY=asia-south1-docker.pkg.dev/kairo-test/kairo,_TAG=abc123def456*'
    }

    It 'runs the Migrator inside the VPC with the connection string' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $job = $calls | Where-Object { $_ -like 'run jobs deploy*' }
        $job | Should -BeLike '*--image asia-south1-docker.pkg.dev/kairo-test/kairo/migrator:abc123def456*'
        $job | Should -BeLike '*--service-account kairo-migrator@kairo-test.iam.gserviceaccount.com*'
        $job | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $job | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest*'
        $job | Should -BeLike '*--max-retries 0*'
    }

    It 'deploys the API privately, inside the VPC, knowing the web address' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $api = $calls | Where-Object { $_ -like 'run deploy kairo-api *' }
        $api | Should -BeLike '*--image asia-south1-docker.pkg.dev/kairo-test/kairo/api:abc123def456*'
        $api | Should -BeLike '*--service-account kairo-api@kairo-test.iam.gserviceaccount.com*'
        $api | Should -BeLike '*--no-allow-unauthenticated*'
        $api | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $api | Should -BeLike '*--session-affinity*'
        $api | Should -BeLike '*--min-instances=1*'
        $api | Should -BeLike '*--set-env-vars=Kairo__PublicOrigin=https://kairo-web-123.asia-south1.run.app*'
        $api | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest,Authentication__Google__ClientId=google-client-id:latest,Authentication__Google__ClientSecret=google-client-secret:latest*'
    }

    It 'leaves Google sign-in off until its secrets have a value' {
        $script:googleConfigured = $false

        Invoke-KairoDeploy -Environment $environment -Root $root 3>$null | Out-Null

        $api = $calls | Where-Object { $_ -like 'run deploy kairo-api *' }
        $api | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest'
        $api | Should -Not -BeLike '*google-client*'
    }

    It 'deploys the worker privately with the database' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $worker = $calls | Where-Object { $_ -like 'run deploy kairo-worker *' }
        $worker | Should -BeLike '*--no-allow-unauthenticated*'
        $worker | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $worker | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest*'
    }

    It 'tells the API and the worker which key encrypts connection tokens' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $key = 'Kairo__Kms__KeyName=projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens'
        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) |
            Should -BeLike "*--set-env-vars=Kairo__PublicOrigin=https://kairo-web-123.asia-south1.run.app,$key*"
        ($calls | Where-Object { $_ -like 'run deploy kairo-worker *' }) | Should -BeLike "*--set-env-vars=$key*"
    }

    It 'deploys the agent service privately with a long timeout' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $agents = $calls | Where-Object { $_ -like 'run deploy kairo-agents *' }
        $agents | Should -BeLike '*--no-allow-unauthenticated*'
        $agents | Should -BeLike '*--timeout 3600*'
    }

    It 'deploys the web app publicly, pointing at the API' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $web = $calls | Where-Object { $_ -like 'run deploy kairo-web *' }
        $web | Should -BeLike '*--no-invoker-iam-check*'
        $web | Should -BeLike '*--min-instances=0*'
        $web | Should -BeLike '*--set-env-vars=KAIRO_API_URL=https://kairo-api-123.asia-south1.run.app,KAIRO_API_AUDIENCE=https://kairo-api-abc123-el.a.run.app*'
    }

    It 'lets live update streams stay open for an hour through the web app and the API' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Where-Object { $_ -like 'run deploy kairo-api *' } | Should -BeLike '*--timeout 3600*'
        $calls | Where-Object { $_ -like 'run deploy kairo-web *' } | Should -BeLike '*--timeout 3600*'
    }

    It 'signs the web app''s calls for the address Cloud Run accepts as the API''s audience' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Should -Contain 'run services describe kairo-api --region asia-south1 --project kairo-test --format=value(status.url)'
    }

    It 'lets only the right callers invoke the private services' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $grants = $calls | Where-Object { $_ -like 'run services add-iam-policy-binding*' }
        $grants | Should -HaveCount 3
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-api --region asia-south1 --project kairo-test --member serviceAccount:kairo-web@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-worker@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
    }

    It 'returns the service addresses' {
        $urls = Invoke-KairoDeploy -Environment $environment -Root $root

        $urls['kairo-web'] | Should -Be 'https://kairo-web-123.asia-south1.run.app'
        $urls['kairo-api'] | Should -Be 'https://kairo-api-123.asia-south1.run.app'
    }
}

Describe 'Invoke-KairoTests' {
    It 'runs every suite and stops at the first failure' {
        $script:ran = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName Kairo.Deploy Invoke-Native {
            $script:ran.Add("$FilePath $($ArgumentList -join ' ')")
            if ($FilePath -eq 'uv' -and $ArgumentList -contains 'pytest') { throw 'pytest failed' }
        }

        { Invoke-KairoTests -Root $root } | Should -Throw 'pytest failed'

        $ran[0] | Should -Be "dotnet test --solution $(Join-Path $root 'core/Kairo.slnx')"
        $ran[1] | Should -Be "uv run --directory $(Join-Path $root 'agents') ruff check"
        $ran[2] | Should -Be "uv run --directory $(Join-Path $root 'agents') ruff format --check"
        $ran[3] | Should -Be "uv run --directory $(Join-Path $root 'agents') pytest"
        $ran | Should -HaveCount 4
    }

    It 'runs the web lint and tests' {
        $script:ran = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName Kairo.Deploy Invoke-Native { $script:ran.Add("$FilePath $($ArgumentList -join ' ')") }

        Invoke-KairoTests -Root $root

        $ran | Should -Contain "npm --prefix $(Join-Path $root 'web') run lint"
        $ran | Should -Contain "npm --prefix $(Join-Path $root 'web') test"
    }
}

Describe 'Assert-KairoCleanTree' {
    It 'refuses uncommitted changes' {
        Mock -ModuleName Kairo.Deploy Invoke-Native { ' M web/src/app/page.tsx' }

        { Assert-KairoCleanTree -Root $root } | Should -Throw 'Commit or stash your changes first*'
    }

    It 'accepts a clean tree' {
        Mock -ModuleName Kairo.Deploy Invoke-Native { }

        { Assert-KairoCleanTree -Root $root } | Should -Not -Throw
    }
}
