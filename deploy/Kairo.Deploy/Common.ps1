$script:Registry = 'kairo'
$script:Network = 'kairo'
$script:Subnet = 'kairo-run'
$script:SqlInstance = 'kairo-sql'
$script:Database = 'kairo'
$script:SqlSecret = 'kairo-sql-connection'
$script:KeyRing = 'kairo'
$script:TokenKey = 'connection-tokens'
$script:MailSecret = 'mail-sender-refresh-token'
$script:ScheduledJobs = @(
    @{ Name = 'kairo-analytics-daily'; Schedule = '0 5 * * *'; Path = '/jobs/analytics/daily'; Deadline = '1800s' }
    @{ Name = 'kairo-notifications-due'; Schedule = '*/15 * * * *'; Path = '/jobs/notifications/due'; Deadline = '600s' }
    @{ Name = 'kairo-catalogue-hourly'; Schedule = '0 * * * *'; Path = '/jobs/catalogue/refresh'; Deadline = '1800s' }
    @{ Name = 'kairo-opportunities-hourly'; Schedule = '30 * * * *'; Path = '/jobs/opportunities/refresh'; Deadline = '1800s' }
    @{ Name = 'kairo-triggers-check'; Schedule = '5,20,35,50 * * * *'; Path = '/jobs/triggers/check'; Deadline = '840s' }
    @{ Name = 'kairo-stage-runs'; Schedule = '*/5 * * * *'; Path = '/jobs/campaigns/stage-runs'; Deadline = '300s' }
    @{ Name = 'kairo-launches'; Schedule = '* * * * *'; Path = '/jobs/launches/run'; Deadline = '300s' }
    @{ Name = 'kairo-launch-health'; Schedule = '*/15 * * * *'; Path = '/jobs/launches/health'; Deadline = '300s' }
    @{ Name = 'kairo-stock-pauses'; Schedule = '*/15 * * * *'; Path = '/jobs/launches/stock'; Deadline = '600s' }
    @{ Name = 'kairo-launch-pacing'; Schedule = '10 * * * *'; Path = '/jobs/launches/pacing'; Deadline = '900s' }
    @{ Name = 'kairo-test-ends'; Schedule = '10 * * * *'; Path = '/jobs/campaigns/test-ends'; Deadline = '600s' }
    @{ Name = 'kairo-recommendations'; Schedule = '25 * * * *'; Path = '/jobs/care/recommendations'; Deadline = '1800s' }
    @{ Name = 'kairo-lift-readings'; Schedule = '40 * * * *'; Path = '/jobs/measurement/lift'; Deadline = '1800s' }
    @{ Name = 'kairo-brand-memory-learn'; Schedule = '50 * * * *'; Path = '/jobs/brand-memory/learn'; Deadline = '1800s' }
    @{ Name = 'kairo-impact-lab'; Schedule = '*/5 * * * *'; Path = '/jobs/impact-lab/run'; Deadline = '300s' }
    @{ Name = 'kairo-ask-previews'; Schedule = '* * * * *'; Path = '/jobs/ask/previews'; Deadline = '1800s' }
)
$script:GoogleSecrets = [ordered]@{
    'Authentication__Google__ClientId'     = 'google-client-id'
    'Authentication__Google__ClientSecret' = 'google-client-secret'
}

function Get-KairoEnvironment {
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$Path = (Join-Path (Split-Path $PSScriptRoot -Parent) 'environments.psd1')
    )

    $environments = Import-PowerShellDataFile -Path $Path
    if (-not $environments.ContainsKey($Name)) {
        throw "Unknown environment '$Name'. Use one of: $(($environments.Keys | Sort-Object) -join ', ')."
    }

    $environment = $environments[$Name].Clone()
    if ([string]::IsNullOrWhiteSpace($environment.Project)) {
        throw "The $Name environment has no project yet. Set its Project in $Path, then run deploy/setup.ps1 $Name."
    }

    $environment.Name = $Name
    $environment
}

function Get-KairoServiceAccount {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    "$Name@$($Environment.Project).iam.gserviceaccount.com"
}

function Get-KairoAgentModels {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $models = [ordered]@{}
    if (-not $Environment.AgentModels) {
        return $models
    }
    foreach ($entry in $Environment.AgentModels.GetEnumerator()) {
        if ($entry.Key -notin @('KAIRO_PLAN_BRIEF_MODEL', 'KAIRO_CHECK_MODEL') -or $entry.Value -isnot [string] -or $entry.Value -notmatch '\Agemini-[a-zA-Z0-9._-]+\z') {
            throw 'Invalid configured brief or Guardian model.'
        }
        $models[$entry.Key] = $entry.Value
    }
    $models
}

function Get-KairoTokenKeyName {
    param([Parameter(Mandatory)][hashtable]$Environment)

    "projects/$($Environment.Project)/locations/$($Environment.Region)/keyRings/$script:KeyRing/cryptoKeys/$script:TokenKey"
}

function Get-KairoServiceUrl {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$ProjectNumber,
        [Parameter(Mandatory)][string]$Service
    )

    "https://$Service-$ProjectNumber.$($Environment.Region).run.app"
}

function Get-KairoRegistry {
    param([Parameter(Mandatory)][hashtable]$Environment)

    "$($Environment.Region)-docker.pkg.dev/$($Environment.Project)/$script:Registry"
}

function Get-KairoImage {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Tag
    )

    "$(Get-KairoRegistry -Environment $Environment)/$($Name):$Tag"
}

function Hide-KairoSecrets {
    param([Parameter(Mandatory)][string]$Text)

    $Text -replace '(--(?:root-)?password=)\S+', '$1***'
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$Capture
    )

    $null = Get-Command -Name $FilePath -ErrorAction Stop
    if ($Capture) {
        $output = & $FilePath @ArgumentList
    }
    else {
        & $FilePath @ArgumentList | Out-Host
    }

    if ($LASTEXITCODE -ne 0) {
        throw (Hide-KairoSecrets -Text "$FilePath $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE.")
    }

    if ($Capture) {
        $output
    }
}

function Invoke-Gcloud {
    param([Parameter(Mandatory)][string[]]$ArgumentList, [switch]$Capture)

    Invoke-Native -FilePath 'gcloud' -ArgumentList (@('--quiet') + $ArgumentList) -Capture:$Capture
}

function Test-Native {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$ArgumentList = @())

    $ErrorActionPreference = 'Continue'
    if (-not (Get-Command -Name $FilePath -ErrorAction SilentlyContinue)) {
        return $false
    }

    & $FilePath @ArgumentList 2>&1 | Out-Null
    $LASTEXITCODE -eq 0
}

function Test-Gcloud {
    param([Parameter(Mandatory)][string[]]$ArgumentList)

    Test-Native -FilePath 'gcloud' -ArgumentList ($ArgumentList + '--quiet')
}

function Get-KairoGenerationSettings {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $limits = [ordered]@{ TextCalls = 100; Images = 50; VideoClips = 10 }
    if ($Environment.GenerationLimits) {
        foreach ($entry in $Environment.GenerationLimits.GetEnumerator()) {
            if ($entry.Key -notin @('TextCalls', 'Images', 'VideoClips') -or $entry.Value -isnot [int] -or $entry.Value -lt 0 -or $entry.Value -gt 1000000) {
                throw 'Generation limits require TextCalls, Images or VideoClips with integer values from 0 to 1000000.'
            }
            $limits[$entry.Key] = $entry.Value
        }
    }
    ($limits.GetEnumerator() | ForEach-Object { "Kairo__Operations__Limits__$($_.Key)=$($_.Value)" }) -join ','
}
