BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{
        Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1'
        SqlTier = 'db-custom-1-3840'; SqlEdition = 'SQLSERVER_2022_EXPRESS'; SqlHighAvailability = $false
        MinInstances = @{ web = 0; api = 1 }
    }
    $script:root = Join-Path ([System.IO.Path]::GetTempPath()) 'Kairo'
}

Describe 'Invoke-KairoDeploy' {
    BeforeEach {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:googleConfigured = $true
        $script:scheduleExists = $false
        Mock -ModuleName Kairo.Deploy Assert-KairoCleanTree { }
        Mock -ModuleName Kairo.Deploy Get-KairoCommit { 'abc123def456' }
        Mock -ModuleName Kairo.Deploy Invoke-KairoTests { $script:calls.Add('tests') }
        Mock -ModuleName Kairo.Deploy Get-KairoProjectNumber { '123' }
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $script:googleConfigured }
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $script:scheduleExists }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $line = $ArgumentList -join ' '
            $script:calls.Add($line)
            if ($line -like 'run services describe kairo-api *') { 'https://kairo-api-abc123-el.a.run.app' }
            if ($line -like 'run services describe kairo-worker *') { 'https://kairo-worker-abc123-el.a.run.app' }
            if ($line -like 'run services describe kairo-agents *') { 'https://kairo-agents-abc123-el.a.run.app' }
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

        foreach ($service in 'kairo-api', 'kairo-worker') {
            $deploy = $calls | Where-Object { $_ -like "run deploy $service *" }
            $deploy | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest'
            $deploy | Should -Not -BeLike '*google-client*'
        }
    }

    It 'deploys the worker privately with the database, the Google client and an hour to finish a run' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $worker = $calls | Where-Object { $_ -like 'run deploy kairo-worker *' }
        $worker | Should -BeLike '*--no-allow-unauthenticated*'
        $worker | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $worker | Should -BeLike '*--timeout 3600*'
        $worker | Should -BeLike '*--no-cpu-throttling*'
        $worker | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest,Authentication__Google__ClientId=google-client-id:latest,Authentication__Google__ClientSecret=google-client-secret:latest*'
    }

    It 'gives the API and the worker enough memory to import a 50 MB catalogue' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -BeLike '*--memory 1Gi*'
        ($calls | Where-Object { $_ -like 'run deploy kairo-worker *' }) | Should -BeLike '*--memory 2Gi*'
    }

    It 'tells the API and the worker where brand datasets live' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        foreach ($service in 'kairo-api', 'kairo-worker') {
            ($calls | Where-Object { $_ -like "run deploy $service *" }) |
                Should -BeLike '*Kairo__Analytics__Project=kairo-test,Kairo__Analytics__Location=asia-south1*'
        }
    }

    It 'tells the API and the worker which project holds the file buckets' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        foreach ($service in 'kairo-api', 'kairo-worker') {
            ($calls | Where-Object { $_ -like "run deploy $service *" }) | Should -BeLike '*Kairo__Files__Project=kairo-test*'
        }
    }

    It 'creates the nightly analytics job when it is missing' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        Should -Invoke -ModuleName Kairo.Deploy Test-Gcloud -ParameterFilter {
            ($ArgumentList -join ' ') -eq 'scheduler jobs describe kairo-analytics-daily --location asia-south1 --project kairo-test'
        }
        $calls | Should -Contain ('scheduler jobs create http kairo-analytics-daily --schedule 0 5 * * * --time-zone Asia/Kolkata ' +
            '--uri https://kairo-worker-abc123-el.a.run.app/jobs/analytics/daily --http-method POST ' +
            '--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com ' +
            '--oidc-token-audience https://kairo-worker-abc123-el.a.run.app --attempt-deadline 1800s --max-retry-attempts 0 ' +
            '--location asia-south1 --project kairo-test')
    }

    It 'creates the notification job that runs every 15 minutes when it is missing' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        Should -Invoke -ModuleName Kairo.Deploy Test-Gcloud -ParameterFilter {
            ($ArgumentList -join ' ') -eq 'scheduler jobs describe kairo-notifications-due --location asia-south1 --project kairo-test'
        }
        $calls | Should -Contain ('scheduler jobs create http kairo-notifications-due --schedule */15 * * * * --time-zone Asia/Kolkata ' +
            '--uri https://kairo-worker-abc123-el.a.run.app/jobs/notifications/due --http-method POST ' +
            '--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com ' +
            '--oidc-token-audience https://kairo-worker-abc123-el.a.run.app --attempt-deadline 600s --max-retry-attempts 0 ' +
            '--location asia-south1 --project kairo-test')
    }

    It 'updates the notification job when it already exists' {
        $script:scheduleExists = $true

        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        ($calls | Where-Object { $_ -like 'scheduler jobs update http kairo-notifications-due *' }) |
            Should -BeLike '*--uri https://kairo-worker-abc123-el.a.run.app/jobs/notifications/due *'
    }

    It 'updates the nightly analytics job when it already exists' {
        $script:scheduleExists = $true

        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        ($calls | Where-Object { $_ -like 'scheduler jobs create *' }) | Should -BeNullOrEmpty
        ($calls | Where-Object { $_ -like 'scheduler jobs update http kairo-analytics-daily *' }) |
            Should -BeLike '*--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com*'
    }

    It 'creates the hourly catalogue job when it is missing' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Should -Contain ('scheduler jobs create http kairo-catalogue-hourly --schedule 0 * * * * --time-zone Asia/Kolkata ' +
            '--uri https://kairo-worker-abc123-el.a.run.app/jobs/catalogue/refresh --http-method POST ' +
            '--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com ' +
            '--oidc-token-audience https://kairo-worker-abc123-el.a.run.app --attempt-deadline 1800s --max-retry-attempts 0 ' +
            '--location asia-south1 --project kairo-test')
    }

    It 'updates the hourly catalogue job when it already exists' {
        $script:scheduleExists = $true

        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        @($calls | Where-Object { $_ -like 'scheduler jobs create http kairo-catalogue-hourly *' }).Count | Should -Be 0
        @($calls | Where-Object { $_ -like 'scheduler jobs update http kairo-catalogue-hourly *' }).Count | Should -Be 1
    }

    It 'tells the API and the worker which key encrypts connection tokens' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $key = 'Kairo__Kms__KeyName=projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens'
        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) |
            Should -BeLike "*--set-env-vars=Kairo__PublicOrigin=https://kairo-web-123.asia-south1.run.app,$key*"
        ($calls | Where-Object { $_ -like 'run deploy kairo-worker *' }) | Should -BeLike "*--set-env-vars=$key*"
    }

    It 'keeps CPU on for the API between requests, so the email loop keeps running' {
        Invoke-KairoDeploy -Environment $environment -Root $root 3>$null | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -BeLike '*--no-cpu-throttling*'
    }

    It 'keeps one API instance running once a sender mailbox can send' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'kairo-alerts@acme.example'
        $withSender.MinInstances = @{ web = 0; api = 0 }

        Invoke-KairoDeploy -Environment $withSender -Root $root | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -BeLike '*--min-instances=1 *'
    }

    It 'lets the API scale to zero while emails are only logged' {
        $quiet = $environment.Clone()
        $quiet.MinInstances = @{ web = 0; api = 0 }

        Invoke-KairoDeploy -Environment $quiet -Root $root 3>$null | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -BeLike '*--min-instances=0 *'
    }

    It 'passes the sender mailbox to the API once it is signed in' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'kairo-alerts@acme.example'

        Invoke-KairoDeploy -Environment $withSender -Root $root | Out-Null

        $api = $calls | Where-Object { $_ -like 'run deploy kairo-api *' }
        $api | Should -BeLike '*,Kairo__Notifications__Sender=kairo-alerts@acme.example*'
        $api | Should -BeLike '*--set-secrets=*,Kairo__Notifications__SenderRefreshToken=mail-sender-refresh-token:latest*'
        ($calls | Where-Object { $_ -like 'run deploy kairo-worker *' }) | Should -Not -BeLike '*Notifications*'
    }

    It 'only logs notification emails until the sender is signed in' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'kairo-alerts@acme.example'
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $Name -ne 'mail-sender-refresh-token' }

        Invoke-KairoDeploy -Environment $withSender -Root $root -WarningAction SilentlyContinue -WarningVariable warnings | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -Not -BeLike '*Notifications*'
        ($warnings -join ' ') | Should -BeLike '*set-mail-sender.ps1*'
    }

    It 'only logs notification emails while the environment names no sender' {
        Invoke-KairoDeploy -Environment $environment -Root $root -WarningAction SilentlyContinue -WarningVariable warnings | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -Not -BeLike '*Notifications*'
        ($warnings -join ' ') | Should -BeLike '*set-mail-sender.ps1*'
    }

    It 'leaves the sender out while Google sign-in is off, since sending needs the OAuth client' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'kairo-alerts@acme.example'
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $Name -eq 'mail-sender-refresh-token' }

        Invoke-KairoDeploy -Environment $withSender -Root $root 3>$null | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -Not -BeLike '*Notifications*'
    }

    It 'deploys the agent service with its project and the Vertex AI location it calls Gemini in' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-agents *' }) |
            Should -BeLike '*--set-env-vars=GOOGLE_CLOUD_PROJECT=kairo-test,GOOGLE_CLOUD_LOCATION=global*'
    }

    It 'tells the API where the agent service is and which audience its tokens need' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) |
            Should -BeLike '*Kairo__Agents__Url=https://kairo-agents-123.asia-south1.run.app,Kairo__Agents__Audience=https://kairo-agents-abc123-el.a.run.app*'
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

    It 'lets each API and web instance hold many idle live update streams' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Where-Object { $_ -like 'run deploy kairo-api *' } | Should -BeLike '*--concurrency 500*'
        $calls | Where-Object { $_ -like 'run deploy kairo-web *' } | Should -BeLike '*--concurrency 500*'
    }

    It 'signs the web app''s calls for the address Cloud Run accepts as the API''s audience' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Should -Contain 'run services describe kairo-api --region asia-south1 --project kairo-test --format=value(status.url)'
    }

    It 'lets only the right callers invoke the private services' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $grants = $calls | Where-Object { $_ -like 'run services add-iam-policy-binding*' }
        $grants | Should -HaveCount 4
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-api --region asia-south1 --project kairo-test --member serviceAccount:kairo-web@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-worker@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-worker --region asia-south1 --project kairo-test --member serviceAccount:kairo-scheduler@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
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
