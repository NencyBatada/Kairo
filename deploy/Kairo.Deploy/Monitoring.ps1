$script:CollectorSecret = 'kairo-otel-config'
$script:CollectorImage = 'us-docker.pkg.dev/cloud-ops-agents-artifacts/google-cloud-opentelemetry-collector/otelcol-google:0.162.0'

function Initialize-KairoTelemetry {
    param([Parameter(Mandatory)][hashtable]$Environment)

    New-KairoSecret -Environment $Environment -Name $script:CollectorSecret
    $config = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'collector.yaml'))
    $current = if (Test-KairoSecretHasValue -Environment $Environment -Name $script:CollectorSecret) {
        Get-KairoSecretValue -Environment $Environment -Name $script:CollectorSecret
    }
    if (("$current" -replace "`r`n", "`n").Trim() -ne ($config -replace "`r`n", "`n").Trim()) {
        Set-KairoSecretValue -Environment $Environment -Name $script:CollectorSecret -Value $config
    }
    foreach ($service in 'kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents') {
        $member = "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $service)"
        foreach ($role in 'roles/cloudtrace.agent', 'roles/monitoring.metricWriter', 'roles/logging.logWriter') {
            Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', $member, '--role', $role, '--condition', 'None')
        }
        Add-KairoBinding -ArgumentList @('secrets', 'add-iam-policy-binding', $script:CollectorSecret, '--member', $member,
            '--role', 'roles/secretmanager.secretAccessor', '--project', $Environment.Project)
    }
}

function Invoke-KairoMonitoringApi {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PATCH')][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [hashtable]$Body
    )

    $token = ((Invoke-Gcloud -ArgumentList @('auth', 'print-access-token') -Capture) -join '').Trim()
    $arguments = @{
        Method = $Method
        Uri = "https://monitoring.googleapis.com/$Path"
        Headers = @{ Authorization = "Bearer $token" }
        ContentType = 'application/json'
        TimeoutSec = 60
    }
    if ($Body) { $arguments.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 40 -Compress)) }
    Invoke-RestMethod @arguments
}

function Get-KairoApplicationContainer {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Service)

    $scope = @('--region', $Environment.Region, '--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('run', 'services', 'describe', $Service) + $scope))) { return 'app' }
    $description = ((Invoke-Gcloud -ArgumentList (@('run', 'services', 'describe', $Service, '--format=json') + $scope) -Capture) -join "`n") | ConvertFrom-Json
    $ingress = @($description.spec.template.spec.containers | Where-Object { $_.ports })
    if ($ingress.Count -eq 1 -and -not $ingress[0].name -and @($description.spec.template.spec.containers).Count -eq 1) { return '' }
    if ($ingress.Count -ne 1 -or -not $ingress[0].name) { throw "Cannot identify $Service's ingress container safely." }
    $ingress[0].name
}

function Initialize-KairoApplicationContainer {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Service)

    $scope = @('--region', $Environment.Region, '--project', $Environment.Project)
    $description = ((Invoke-Gcloud -ArgumentList (@('run', 'services', 'describe', $Service, '--format=json') + $scope) -Capture) -join "`n") | ConvertFrom-Json
    $containers = @($description.spec.template.spec.containers)
    if ($containers.Count -ne 1 -or $containers[0].name -or -not $containers[0].ports) {
        throw "$Service changed before its unnamed ingress could be migrated; inspect it before retrying."
    }
    $containers[0] | Add-Member -NotePropertyName name -NotePropertyValue app -Force
    $description.spec.template.metadata.PSObject.Properties.Remove('name')
    $replacement = @{
        apiVersion = $description.apiVersion
        kind = $description.kind
        metadata = @{
            name = $description.metadata.name
            namespace = $description.metadata.namespace
            resourceVersion = $description.metadata.resourceVersion
            labels = $description.metadata.labels
            annotations = $description.metadata.annotations
        }
        spec = $description.spec
    }
    $file = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($file, ($replacement | ConvertTo-Json -Depth 50))
        Invoke-Gcloud -ArgumentList (@('run', 'services', 'replace', $file) + $scope) | Out-Null
    }
    finally { Remove-Item -LiteralPath $file -Force }
    'app'
}

function Set-KairoMonitoringResource {
    param(
        [Parameter(Mandatory)][string]$Collection,
        [Parameter(Mandatory)][string]$ResponseField,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][hashtable]$Body
    )

    $owned = @()
    $path = "$Version/$Collection"
    do {
        $response = Invoke-KairoMonitoringApi -Method GET -Path $path
        $owned += @($response.$ResponseField | Where-Object {
            $_.displayName -eq $Body.displayName -and
            ($_.userLabels.managed_by -eq 'kairo' -or $_.labels.managed_by -eq 'kairo')
        })
        $path = if ($response.nextPageToken) { "$Version/${Collection}?pageToken=$([Uri]::EscapeDataString($response.nextPageToken))" }
    } while ($path)
    if ($owned.Count -gt 1) { throw "Multiple managed resources named $($Body.displayName); resolve duplicates before deployment." }
    if ($owned.Count -eq 1) {
        $Body.name = $owned[0].name
        foreach ($condition in $Body.conditions) {
            $previous = @($owned[0].conditions | Where-Object { $_.displayName -eq $condition.displayName })
            if ($previous.Count -eq 1 -and $previous[0].name) { $condition.name = $previous[0].name }
        }
        if ($owned[0].etag) { $Body.etag = $owned[0].etag }
        Invoke-KairoMonitoringApi -Method PATCH -Path "$Version/$($Body.name)" -Body $Body
    }
    else {
        Invoke-KairoMonitoringApi -Method POST -Path "$Version/$Collection" -Body $Body
    }
}

function New-KairoPromqlPolicy {
    param([string]$Name, [string]$Query, [string]$Duration = '0s')

    @{
        displayName = $Name
        enabled = $true
        combiner = 'OR'
        conditions = @(@{
            displayName = $Name
            conditionPrometheusQueryLanguage = @{ query = $Query; duration = $Duration; evaluationInterval = '60s'; disableMetricValidation = ($Query -match 'kairo_') }
        })
    }
}

function Get-KairoMonitoringDefinitions {
    param([Parameter(Mandatory)][hashtable]$Environment, [string[]]$Channels = @())

    $labels = @{ managed_by = 'kairo'; environment = $Environment.Name }
    $serviceFilter = 'resource.type="cloud_run_revision" resource.labels.service_name=~"^kairo-(web|api|worker|agents)$"'
    $policies = @(
        foreach ($event in 'launch.failed', 'stage.failed_twice', 'mass_pause.held', 'feed.stale', 'connection.expired', 'integration.quota_exceeded') {
            @{
                displayName = "Kairo $($Environment.Name): $event"
                enabled = $true
                combiner = 'OR'
                conditions = @(@{ displayName = $event; conditionMatchedLog = @{ filter = "$serviceFilter jsonPayload.event=`"$event`"" } })
                alertStrategy = @{ notificationRateLimit = @{ period = '300s' }; autoClose = '1800s' }
            }
        }
        New-KairoPromqlPolicy -Name "Kairo $($Environment.Name): queue backlog" -Query 'max(kairo_stage_queue_age_seconds) > 300' -Duration '600s'
        New-KairoPromqlPolicy -Name "Kairo $($Environment.Name): service errors" -Query 'sum(increase({"run.googleapis.com/request_count", monitored_resource="cloud_run_revision", service_name=~"kairo-(web|api|worker|agents)", response_code_class="5xx"}[5m])) > 0'
        New-KairoPromqlPolicy -Name "Kairo $($Environment.Name): quota allocation above 80%" -Query '(max by (project_id, quota_metric, location) ({"serviceruntime.googleapis.com/quota/allocation/usage", monitored_resource="consumer_quota"}) / min by (project_id, quota_metric, location) ({"serviceruntime.googleapis.com/quota/limit", monitored_resource="consumer_quota"})) > 0.8'
        New-KairoPromqlPolicy -Name "Kairo $($Environment.Name): minute quota above 80%" -Query '(sum by (project_id, service, quota_metric, location) (increase({"serviceruntime.googleapis.com/quota/rate/net_usage", monitored_resource="consumer_quota"}[1m])) / min by (project_id, service, quota_metric, location) ({"serviceruntime.googleapis.com/quota/limit", monitored_resource="consumer_quota", limit_name=~".*PerMinutePerProject.*|.*QueriesPerMinute$"})) > 0.8'
        New-KairoPromqlPolicy -Name "Kairo $($Environment.Name): daily quota above 80%" -Query '(sum by (project_id, service, quota_metric, location) (increase({"serviceruntime.googleapis.com/quota/rate/net_usage", monitored_resource="consumer_quota"}[1d])) / min by (project_id, service, quota_metric, location) ({"serviceruntime.googleapis.com/quota/limit", monitored_resource="consumer_quota", limit_name=~".*PerDay$|.*PerDayPerProject.*|explorer-access-level-operation-limit-per-project-test"})) > 0.8'
    )
    foreach ($policy in $policies) {
        $policy.userLabels = $labels.Clone()
        $policy.notificationChannels = @($Channels)
        $policy.documentation = @{
            mimeType = 'text/markdown'
            content = "Inspect Kairo's operations dashboard and the incident's correlated trace/logs. Check saved job state before retrying. Never bypass campaign approvals."
        }
    }
    $charts = [ordered]@{
        'Stage duration by kind (p95 seconds)' = 'histogram_quantile(0.95, sum by (le, stage) (rate(kairo_stage_duration_seconds_bucket[5m])))'
        'Oldest eligible stage (seconds)' = 'max(kairo_stage_queue_age_seconds)'
        'Eligible queued stages' = 'sum(kairo_stage_queue_count)'
        'Agent request duration (p95 seconds)' = 'histogram_quantile(0.95, sum by (le, operation) (rate(kairo_agent_duration_seconds_bucket[5m])))'
        'Model latency (p95 seconds)' = 'histogram_quantile(0.95, sum by (le, model) (rate(kairo_model_duration_seconds_bucket[5m])))'
        'Model failures' = 'sum by (model) (increase(kairo_model_duration_seconds_count{outcome="failed"}[15m]))'
        'Operational events and launch outcomes' = 'sum by (event) (increase(kairo_operations_total[15m]))'
        'Google integration request failures' = 'sum by (event) (increase(kairo_operations_total{event=~"integration.*|connection.expired"}[15m]))'
        'Cloud Run request errors' = 'sum by (service_name) (increase({"run.googleapis.com/request_count", monitored_resource="cloud_run_revision", response_code_class="5xx", service_name=~"kairo-.*"}[5m]))'
    }
    $tiles = @()
    $index = 0
    foreach ($entry in $charts.GetEnumerator()) {
        $tiles += @{
            xPos = ($index % 2) * 6; yPos = [int][Math]::Floor($index / 2) * 4; width = 6; height = 4
            widget = @{ title = $entry.Key; xyChart = @{ dataSets = @(@{ timeSeriesQuery = @{ prometheusQuery = $entry.Value } }); chartOptions = @{ mode = 'COLOR' } } }
        }
        $index++
    }
    $tiles += @{
        xPos = 0; yPos = [int][Math]::Ceiling($index / 2) * 4; width = 12; height = 4
        widget = @{ title = 'Correlated operational logs'; logsPanel = @{ filter = "$serviceFilter severity>=WARNING"; resourceNames = @("projects/$($Environment.Project)") } }
    }
    @{
        Policies = $policies
        Dashboard = @{ displayName = "Kairo $($Environment.Name): operations"; labels = $labels; mosaicLayout = @{ columns = 12; tiles = $tiles } }
    }
}

function Publish-KairoMonitoring {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $project = "projects/$($Environment.Project)"
    $channels = @()
    if ($Environment.OperationsEmail) {
        $channel = Set-KairoMonitoringResource -Collection "$project/notificationChannels" -ResponseField notificationChannels -Version v3 -Body @{
            displayName = "Kairo $($Environment.Name): operations email"
            type = 'email'; enabled = $true
            labels = @{ email_address = $Environment.OperationsEmail }
            userLabels = @{ managed_by = 'kairo'; environment = $Environment.Name }
        }
        $channels += $channel.name
    }
    $definitions = Get-KairoMonitoringDefinitions -Environment $Environment -Channels $channels
    foreach ($policy in $definitions.Policies) {
        Set-KairoMonitoringResource -Collection "$project/alertPolicies" -ResponseField alertPolicies -Version v3 -Body $policy | Out-Null
    }
    Set-KairoMonitoringResource -Collection "$project/dashboards" -ResponseField dashboards -Version v1 -Body $definitions.Dashboard | Out-Null
}
