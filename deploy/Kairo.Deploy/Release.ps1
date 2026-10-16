$script:Vpc = @('--network', 'kairo', '--subnet', 'kairo-run', '--vpc-egress', 'private-ranges-only')
$script:AiLocation = 'global'

function Invoke-KairoDeploy {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Root,
        [switch]$SkipTests
    )

    Assert-KairoCleanTree -Root $Root
    $tag = Get-KairoCommit -Root $Root

    if ($SkipTests) {
        Write-Warning 'Skipping the tests.'
    }
    else {
        Invoke-KairoTests -Root $Root
    }

    $number = Get-KairoProjectNumber -Environment $Environment
    $urls = @{}
    foreach ($service in 'kairo-web', 'kairo-api', 'kairo-agents', 'kairo-worker') {
        $urls[$service] = Get-KairoServiceUrl -Environment $Environment -ProjectNumber $number -Service $service
    }

    Invoke-KairoBuild -Environment $Environment -Root $Root -Tag $tag
    Invoke-KairoMigration -Environment $Environment -Tag $tag

    $sqlSecret = "ConnectionStrings__kairo=$($script:SqlSecret):latest"
    $keyName = Get-KairoTokenKeyName -Environment $Environment
    $analytics = "Kairo__Analytics__Project=$($Environment.Project),Kairo__Analytics__Location=$($Environment.Region)"
    $files = "Kairo__Files__Project=$($Environment.Project)"
    $serviceSecrets = @($sqlSecret)
    $googleConfigured = $true
    foreach ($secret in $script:GoogleSecrets.Values) {
        if (-not (Test-KairoSecretHasValue -Environment $Environment -Name $secret)) {
            $googleConfigured = $false
        }
    }
    if ($googleConfigured) {
        foreach ($entry in $script:GoogleSecrets.GetEnumerator()) {
            $serviceSecrets += "$($entry.Key)=$($entry.Value):latest"
        }
    }
    $apiSettings = "Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName,$analytics,$files"
    $apiSecrets = @($serviceSecrets)
    $mailSender = "$($Environment.MailSender)".Trim()
    $mailConfigured = $googleConfigured -and $mailSender -and (Test-KairoSecretHasValue -Environment $Environment -Name $script:MailSecret)
    if ($mailConfigured) {
        $apiSettings += ",Kairo__Notifications__Sender=$mailSender"
        $apiSecrets += "Kairo__Notifications__SenderRefreshToken=$($script:MailSecret):latest"
    }
    $apiMinInstances = if ($mailConfigured) { [Math]::Max(1, [int]$Environment.MinInstances.api) } else { $Environment.MinInstances.api }

    Publish-KairoService -Environment $Environment -Service 'kairo-agents' -Image (Get-KairoImage -Environment $Environment -Name 'agents' -Tag $tag) `
        -Settings @('--no-allow-unauthenticated', '--timeout', '3600', '--memory', '2Gi', '--cpu', '2', "--set-env-vars=GOOGLE_CLOUD_PROJECT=$($Environment.Project),GOOGLE_CLOUD_LOCATION=$($script:AiLocation)")

    $agentsAudience = Get-KairoServiceAudience -Environment $Environment -Service 'kairo-agents'
    $agentsSettings = "Kairo__Agents__Url=$($urls['kairo-agents']),Kairo__Agents__Audience=$agentsAudience"
    $apiSettings += ",$agentsSettings"

    Publish-KairoService -Environment $Environment -Service 'kairo-api' -Image (Get-KairoImage -Environment $Environment -Name 'api' -Tag $tag) `
        -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @(
            '--session-affinity',
            '--timeout', '3600',
            '--concurrency', '500',
            '--no-cpu-throttling',
            '--memory', '1Gi',
            "--min-instances=$apiMinInstances",
            "--set-env-vars=$apiSettings",
            "--set-secrets=$($apiSecrets -join ',')"
        ))

    $apiAudience = Get-KairoServiceAudience -Environment $Environment -Service 'kairo-api'

    Publish-KairoService -Environment $Environment -Service 'kairo-worker' -Image (Get-KairoImage -Environment $Environment -Name 'worker' -Tag $tag) `
        -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @(
            '--timeout', '3600',
            '--no-cpu-throttling',
            '--memory', '2Gi',
            "--set-env-vars=Kairo__Kms__KeyName=$keyName,$analytics,$files,$agentsSettings",
            "--set-secrets=$($serviceSecrets -join ',')"
        ))

    Publish-KairoService -Environment $Environment -Service 'kairo-web' -Image (Get-KairoImage -Environment $Environment -Name 'web' -Tag $tag) `
        -Settings @(
            '--no-invoker-iam-check',
            '--timeout', '3600',
            '--concurrency', '500',
            "--min-instances=$($Environment.MinInstances.web)",
            "--set-env-vars=KAIRO_API_URL=$($urls['kairo-api']),KAIRO_API_AUDIENCE=$apiAudience"
        )

    Grant-KairoInvoker -Environment $Environment -Service 'kairo-api' -Caller 'kairo-web'
    Grant-KairoInvoker -Environment $Environment -Service 'kairo-agents' -Caller 'kairo-api'
    Grant-KairoInvoker -Environment $Environment -Service 'kairo-agents' -Caller 'kairo-worker'
    Grant-KairoInvoker -Environment $Environment -Service 'kairo-worker' -Caller 'kairo-scheduler'
    Publish-KairoSchedule -Environment $Environment -WorkerUrl (Get-KairoServiceAudience -Environment $Environment -Service 'kairo-worker')

    Write-Host ''
    Write-Host "Kairo $tag is live in $($Environment.Name) at $($urls['kairo-web'])"
    Write-Host "Google sign-in redirect URI: $($urls['kairo-web'])/api/auth/callback/google"
    Write-Host "Google account connections redirect URI: $($urls['kairo-web'])/api/connections/callback/google"
    if (-not $googleConfigured) {
        Write-Warning 'Google sign-in is off until google-client-id and google-client-secret have a value. See "Deploy" in README.md.'
    }
    if (-not $mailConfigured) {
        Write-Warning 'Notification emails are only logged until deploy/set-mail-sender.ps1 has been run. See "Set up notification emails" in README.md.'
    }

    $urls
}

function Assert-KairoCleanTree {
    param([Parameter(Mandatory)][string]$Root)

    $changes = Invoke-Native -FilePath 'git' -ArgumentList @('-C', $Root, 'status', '--porcelain') -Capture
    if ($changes) {
        throw "Commit or stash your changes first, so the image tag names exactly what is deployed:`n$($changes -join "`n")"
    }
}

function Get-KairoCommit {
    param([Parameter(Mandatory)][string]$Root)

    ((Invoke-Native -FilePath 'git' -ArgumentList @('-C', $Root, 'rev-parse', '--short=12', 'HEAD') -Capture) | Select-Object -First 1).Trim()
}

function Invoke-KairoTests {
    param([Parameter(Mandatory)][string]$Root)

    $agents = Join-Path $Root 'agents'
    $web = Join-Path $Root 'web'
    Invoke-Native -FilePath 'dotnet' -ArgumentList @('test', '--solution', (Join-Path $Root 'core/Kairo.slnx'))
    Invoke-Native -FilePath 'uv' -ArgumentList @('run', '--directory', $agents, 'ruff', 'check')
    Invoke-Native -FilePath 'uv' -ArgumentList @('run', '--directory', $agents, 'ruff', 'format', '--check')
    Invoke-Native -FilePath 'uv' -ArgumentList @('run', '--directory', $agents, 'pytest')
    Invoke-Native -FilePath 'npm' -ArgumentList @('--prefix', $web, 'run', 'lint')
    Invoke-Native -FilePath 'npm' -ArgumentList @('--prefix', $web, 'test')
}

function Get-KairoProjectNumber {
    param([Parameter(Mandatory)][hashtable]$Environment)

    ((Invoke-Gcloud -ArgumentList @('projects', 'describe', $Environment.Project, '--format=value(projectNumber)') -Capture) | Select-Object -First 1).Trim()
}

function Invoke-KairoBuild {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Tag
    )

    Write-Host "Building images for $Tag"
    Invoke-Gcloud -ArgumentList @(
        'builds', 'submit', $Root,
        '--config', (Join-Path $Root 'deploy/cloudbuild.yaml'),
        '--region', $Environment.Region,
        '--project', $Environment.Project,
        '--service-account', "projects/$($Environment.Project)/serviceAccounts/$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-build')",
        '--gcs-source-staging-dir', "gs://$($Environment.Project)-builds/source",
        "--substitutions=_REGISTRY=$(Get-KairoRegistry -Environment $Environment),_TAG=$Tag"
    )
}

function Invoke-KairoMigration {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Tag)

    $scope = @('--region', $Environment.Region, '--project', $Environment.Project)
    Write-Host 'Applying database migrations'
    Invoke-Gcloud -ArgumentList (@(
            'run', 'jobs', 'deploy', 'kairo-migrator',
            '--image', (Get-KairoImage -Environment $Environment -Name 'migrator' -Tag $Tag),
            '--service-account', (Get-KairoServiceAccount -Environment $Environment -Name 'kairo-migrator')
        ) + $scope + $script:Vpc + @(
            "--set-secrets=ConnectionStrings__kairo=$($script:SqlSecret):latest",
            '--max-retries', '0',
            '--task-timeout', '900'
        ))
    Invoke-Gcloud -ArgumentList (@('run', 'jobs', 'execute', 'kairo-migrator') + $scope + @('--wait'))
}

function Publish-KairoService {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Service,
        [Parameter(Mandatory)][string]$Image,
        [string[]]$Settings = @()
    )

    Write-Host "Deploying $Service"
    Invoke-Gcloud -ArgumentList (@(
            'run', 'deploy', $Service,
            '--image', $Image,
            '--service-account', (Get-KairoServiceAccount -Environment $Environment -Name $Service),
            '--region', $Environment.Region,
            '--project', $Environment.Project
        ) + $Settings)
}

function Get-KairoServiceAudience {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Service)

    ((Invoke-Gcloud -ArgumentList @(
                'run', 'services', 'describe', $Service,
                '--region', $Environment.Region,
                '--project', $Environment.Project,
                '--format=value(status.url)'
            ) -Capture) | Select-Object -First 1).Trim()
}

function Grant-KairoInvoker {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Service,
        [Parameter(Mandatory)][string]$Caller
    )

    Invoke-Gcloud -ArgumentList @(
        'run', 'services', 'add-iam-policy-binding', $Service,
        '--region', $Environment.Region,
        '--project', $Environment.Project,
        '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $Caller)",
        '--role', 'roles/run.invoker'
    )
}

function Publish-KairoSchedule {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$WorkerUrl
    )

    $scope = @('--location', $Environment.Region, '--project', $Environment.Project)
    foreach ($job in $script:ScheduledJobs) {
        $verb = 'create'
        if (Test-Gcloud -ArgumentList (@('scheduler', 'jobs', 'describe', $job.Name) + $scope)) {
            $verb = 'update'
        }

        Write-Host "Scheduling $($job.Name)"
        Invoke-Gcloud -ArgumentList (@(
                'scheduler', 'jobs', $verb, 'http', $job.Name,
                '--schedule', $job.Schedule,
                '--time-zone', 'Asia/Kolkata',
                '--uri', "$WorkerUrl$($job.Path)",
                '--http-method', 'POST',
                '--oidc-service-account-email', (Get-KairoServiceAccount -Environment $Environment -Name 'kairo-scheduler'),
                '--oidc-token-audience', $WorkerUrl,
                '--attempt-deadline', $job.Deadline,
                '--max-retry-attempts', '0'
            ) + $scope)
    }
}
