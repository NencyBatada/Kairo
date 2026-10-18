BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{
        Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1'
        OperationsEmail = 'operations@example.com'
    }
}

Describe 'Monitoring definitions' {
    It 'routes all policies to operations and covers the agreed incidents and thresholds' {
        $definitions = Get-KairoMonitoringDefinitions -Environment $environment -Channels 'projects/kairo-test/notificationChannels/1'
        $definitions.Policies.Count | Should -Be 11
        foreach ($policy in $definitions.Policies) {
            $policy.notificationChannels | Should -Contain 'projects/kairo-test/notificationChannels/1'
            $policy.userLabels.managed_by | Should -Be 'kairo'
            $policy.enabled | Should -BeTrue
        }
        $logRules = $definitions.Policies | Where-Object { $_.conditions[0].conditionMatchedLog }
        $logRules.Count | Should -Be 6
        foreach ($event in 'launch.failed', 'stage.failed_twice', 'mass_pause.held', 'feed.stale', 'connection.expired', 'integration.quota_exceeded') {
            ($logRules.conditions.conditionMatchedLog.filter -join ' ') | Should -BeLike "*$event*"
        }
        $queue = $definitions.Policies | Where-Object { $_.displayName -like '*queue backlog' }
        $queue.conditions[0].conditionPrometheusQueryLanguage.query | Should -Be 'max(kairo_stage_queue_age_seconds) > 300'
        $queue.conditions[0].conditionPrometheusQueryLanguage.duration | Should -Be '600s'
        $queue.conditions[0].conditionPrometheusQueryLanguage.disableMetricValidation | Should -BeTrue
        foreach ($quota in ($definitions.Policies | Where-Object { $_.displayName -like '*80%' })) {
            $quota.conditions[0].conditionPrometheusQueryLanguage.query | Should -BeLike '* > 0.8'
        }
        $definitions.Dashboard.mosaicLayout.tiles.widget.title | Should -Contain 'Model failures'
        $definitions.Dashboard.mosaicLayout.tiles.widget.title | Should -Contain 'Correlated operational logs'
    }
}

Describe 'Monitoring resource ownership and updates' {
    BeforeEach {
        $script:requests = [System.Collections.Generic.List[object]]::new()
        $script:existing = @()
        Mock -ModuleName Kairo.Deploy Invoke-KairoMonitoringApi {
            $script:requests.Add(@{ Method = $Method; Path = $Path; Body = $Body })
            if ($Method -eq 'GET') { return @{ alertPolicies = $script:existing } }
            return @{ name = 'projects/kairo-test/alertPolicies/123' }
        }
    }

    It 'creates a resource only when no owned resource exists' {
        $script:existing = @(@{ displayName = 'operations'; name = 'projects/kairo-test/alertPolicies/someone-else'; userLabels = @{} })
        Set-KairoMonitoringResource -Collection 'projects/kairo-test/alertPolicies' -ResponseField alertPolicies -Version v3 -Body @{
            displayName = 'operations'; userLabels = @{ managed_by = 'kairo' }
        } | Out-Null
        $requests[1].Method | Should -Be 'POST'
        $requests[1].Body.ContainsKey('name') | Should -BeFalse
    }

    It 'patches the existing owned policy instead of creating another' {
        $script:existing = @(@{ displayName = 'operations'; name = 'projects/kairo-test/alertPolicies/123'; userLabels = @{ managed_by = 'kairo' } })
        Set-KairoMonitoringResource -Collection 'projects/kairo-test/alertPolicies' -ResponseField alertPolicies -Version v3 -Body @{
            displayName = 'operations'; userLabels = @{ managed_by = 'kairo' }
        } | Out-Null
        $requests[1].Method | Should -Be 'PATCH'
        $requests[1].Path | Should -Be 'v3/projects/kairo-test/alertPolicies/123'
        $requests[1].Body.name | Should -Be 'projects/kairo-test/alertPolicies/123'
    }

    It 'fails on ambiguous owned resources without overwriting either' {
        $script:existing = @(
            @{ displayName = 'operations'; name = 'one'; userLabels = @{ managed_by = 'kairo' } },
            @{ displayName = 'operations'; name = 'two'; userLabels = @{ managed_by = 'kairo' } }
        )
        { Set-KairoMonitoringResource -Collection 'projects/kairo-test/alertPolicies' -ResponseField alertPolicies -Version v3 -Body @{ displayName = 'operations' } } |
            Should -Throw '*Multiple managed resources*'
        ($requests | Where-Object { $_.Method -ne 'GET' }) | Should -BeNullOrEmpty
    }
}

Describe 'Collector deployment' {
    It 'recognizes the legacy single unnamed ingress' {
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $true }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            '{"spec":{"template":{"spec":{"containers":[{"image":"original","ports":[{"containerPort":8080}]}]}}}}'
        }
        Get-KairoApplicationContainer -Environment $environment -Service kairo-api | Should -BeNullOrEmpty
    }

    It 'names a legacy ingress without changing IAM, networking, traffic, secrets or its image' {
        $script:replacement = $null
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            if ($ArgumentList[2] -eq 'describe') {
                return '{"apiVersion":"serving.knative.dev/v1","kind":"Service","metadata":{"name":"kairo-api","namespace":"kairo-test","resourceVersion":"42","annotations":{"run.googleapis.com/ingress":"internal"}},"spec":{"traffic":[{"latestRevision":true,"percent":100}],"template":{"metadata":{"name":"old-revision","annotations":{"run.googleapis.com/network-interfaces":"private"}},"spec":{"serviceAccountName":"api@kairo-test","containers":[{"image":"original","ports":[{"containerPort":8080}],"env":[{"name":"secret","valueFrom":{"secretKeyRef":{"name":"secret-ref","key":"latest"}}}]}]}}}}'
            }
            $script:replacement = Get-Content -Raw -LiteralPath $ArgumentList[3] | ConvertFrom-Json
        }
        Initialize-KairoApplicationContainer -Environment $environment -Service kairo-api | Should -Be 'app'
        $replacement.spec.template.spec.containers[0].name | Should -Be 'app'
        $replacement.spec.template.spec.containers[0].image | Should -Be 'original'
        $replacement.spec.template.spec.containers[0].env[0].valueFrom.secretKeyRef.name | Should -Be 'secret-ref'
        $replacement.metadata.resourceVersion | Should -Be '42'
        $replacement.metadata.annotations.'run.googleapis.com/ingress' | Should -Be 'internal'
        $replacement.spec.template.metadata.annotations.'run.googleapis.com/network-interfaces' | Should -Be 'private'
        $replacement.spec.template.metadata.name | Should -BeNullOrEmpty
        $replacement.spec.traffic[0].percent | Should -Be 100
    }
    It 'retains the existing ingress container when adding the sidecar' {
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $true }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            '{"spec":{"template":{"spec":{"containers":[{"name":"kairo-api-1","ports":[{"containerPort":8080}]},{"name":"collector"}]}}}}'
        }
        Get-KairoApplicationContainer -Environment $environment -Service kairo-api | Should -Be 'kairo-api-1'
    }

    It 'rejects ambiguous ingress containers without modifying the service' {
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $true }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { '{"spec":{"template":{"spec":{"containers":[{"name":"one","ports":[{}]},{"name":"two","ports":[{}]}]}}}}' }
        { Get-KairoApplicationContainer -Environment $environment -Service kairo-api } | Should -Throw '*Cannot identify*'
    }
    It 'keeps IAM and networking service-wide while mounting config only in the private sidecar' {
        $script:arguments = @()
        Mock -ModuleName Kairo.Deploy Get-KairoApplicationContainer { 'app' }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { $script:arguments = $ArgumentList }
        Publish-KairoService -Environment $environment -Service kairo-api -Image app-image -Settings @(
            '--no-allow-unauthenticated', '--memory', '1Gi', '--set-env-vars=Kairo__PublicOrigin=https://web.example.com'
        )
        $arguments.IndexOf('--no-allow-unauthenticated') | Should -BeLessThan $arguments.IndexOf('--container')
        $arguments -join ' ' | Should -BeLike '*--container app --image app-image --port 8080 --depends-on collector*'
        $arguments -join ' ' | Should -BeLike '*OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318*'
        $arguments -join ' ' | Should -BeLike '*--container collector*--set-secrets=/etc/otel/config.yaml=kairo-otel-config:latest*'
        $arguments | Should -Contain '--no-cpu-throttling'
    }
}
