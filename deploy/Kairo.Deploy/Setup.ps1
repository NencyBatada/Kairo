$script:Apis = @(
    'run.googleapis.com', 'sqladmin.googleapis.com', 'compute.googleapis.com', 'servicenetworking.googleapis.com',
    'artifactregistry.googleapis.com', 'cloudbuild.googleapis.com', 'secretmanager.googleapis.com',
    'iam.googleapis.com', 'storage.googleapis.com', 'cloudkms.googleapis.com',
    'googleads.googleapis.com', 'youtube.googleapis.com', 'merchantapi.googleapis.com', 'analyticsadmin.googleapis.com',
    'bigquery.googleapis.com'
)
$script:ServiceAccounts = @('kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build')
$script:PasswordCharacters = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'

function Initialize-KairoProject {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)

    Write-Host "Enabling APIs in $($Environment.Project)"
    Invoke-Gcloud -ArgumentList (@('services', 'enable') + $script:Apis + $scope)

    Initialize-KairoRegistry -Environment $Environment
    Initialize-KairoNetwork -Environment $Environment
    Initialize-KairoDatabase -Environment $Environment
    Initialize-KairoSecrets -Environment $Environment
    Initialize-KairoServiceAccounts -Environment $Environment
    Initialize-KairoKeys -Environment $Environment
}

function Initialize-KairoRegistry {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('artifacts', 'repositories', 'describe', $script:Registry, '--location', $Environment.Region) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('artifacts', 'repositories', 'create', $script:Registry, '--repository-format', 'docker', '--location', $Environment.Region) + $scope)
    }

    $bucket = "gs://$($Environment.Project)-builds"
    if (-not (Test-Gcloud -ArgumentList (@('storage', 'buckets', 'describe', $bucket) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('storage', 'buckets', 'create', $bucket, '--location', $Environment.Region, '--uniform-bucket-level-access') + $scope)
    }
}

function Initialize-KairoNetwork {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('compute', 'networks', 'describe', $script:Network) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('compute', 'networks', 'create', $script:Network, '--subnet-mode', 'custom') + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('compute', 'networks', 'subnets', 'describe', $script:Subnet, '--region', $Environment.Region) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('compute', 'networks', 'subnets', 'create', $script:Subnet, '--network', $script:Network, '--region', $Environment.Region, '--range', '10.10.0.0/24') + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('compute', 'addresses', 'describe', 'kairo-google-services', '--global') + $scope))) {
        Invoke-Gcloud -ArgumentList (@('compute', 'addresses', 'create', 'kairo-google-services', '--global', '--purpose', 'VPC_PEERING', '--prefix-length', '20', '--network', $script:Network) + $scope)
    }

    $peerings = Invoke-Gcloud -ArgumentList (@('services', 'vpc-peerings', 'list', '--network', $script:Network) + $scope) -Capture
    if (-not ($peerings -match 'servicenetworking')) {
        Invoke-Gcloud -ArgumentList (@('services', 'vpc-peerings', 'connect', '--service', 'servicenetworking.googleapis.com', '--ranges', 'kairo-google-services', '--network', $script:Network) + $scope)
    }
}

function Initialize-KairoDatabase {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    $hasInstance = Test-Gcloud -ArgumentList (@('sql', 'instances', 'describe', $script:SqlInstance) + $scope)
    $hasSecret = (Test-Gcloud -ArgumentList (@('secrets', 'describe', $script:SqlSecret) + $scope)) -and
        (Test-KairoSecretHasValue -Environment $Environment -Name $script:SqlSecret)

    $password = $null
    if (-not $hasInstance) {
        $password = New-KairoPassword
        $availability = 'zonal'
        if ($Environment.SqlHighAvailability) {
            $availability = 'regional'
        }

        Write-Host "Creating Cloud SQL instance $script:SqlInstance. This takes about ten minutes."
        Invoke-Gcloud -ArgumentList (@(
                'sql', 'instances', 'create', $script:SqlInstance,
                '--database-version', $Environment.SqlEdition,
                '--edition', 'enterprise',
                '--tier', $Environment.SqlTier,
                '--region', $Environment.Region,
                '--network', "projects/$($Environment.Project)/global/networks/$script:Network",
                '--no-assign-ip',
                '--storage-type', 'SSD',
                '--storage-size', '10',
                '--storage-auto-increase',
                '--backup-start-time', '20:30',
                '--availability-type', $availability,
                "--root-password=$password"
            ) + $scope)
    }
    elseif (-not $hasSecret) {
        $password = New-KairoPassword
        Invoke-Gcloud -ArgumentList (@('sql', 'users', 'set-password', 'sqlserver', '--instance', $script:SqlInstance, "--password=$password") + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('sql', 'databases', 'describe', $script:Database, '--instance', $script:SqlInstance) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('sql', 'databases', 'create', $script:Database, '--instance', $script:SqlInstance) + $scope)
    }

    if ($password) {
        $address = Get-KairoSqlAddress -Environment $Environment
        Set-KairoSecretValue -Environment $Environment -Name $script:SqlSecret `
            -Value "Server=$address;Database=$script:Database;User Id=sqlserver;Password=$password;Encrypt=True;TrustServerCertificate=True"
    }
}

function Get-KairoSqlAddress {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $json = (Invoke-Gcloud -ArgumentList @('sql', 'instances', 'describe', $script:SqlInstance, '--format=json', '--project', $Environment.Project) -Capture) -join "`n"
    $address = ($json | ConvertFrom-Json).ipAddresses | Where-Object { $_.type -eq 'PRIVATE' } | Select-Object -First 1
    if (-not $address) {
        throw "Cloud SQL instance $script:SqlInstance has no private IP address."
    }

    $address.ipAddress
}

function Initialize-KairoSecrets {
    param([Parameter(Mandatory)][hashtable]$Environment)

    foreach ($secret in $script:GoogleSecrets.Values) {
        New-KairoSecret -Environment $Environment -Name $secret
    }
}

function New-KairoSecret {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    $scope = @('--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('secrets', 'describe', $Name) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('secrets', 'create', $Name, '--replication-policy', 'automatic') + $scope)
    }
}

function Set-KairoSecretValue {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    New-KairoSecret -Environment $Environment -Name $Name
    $file = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllBytes($file, [Text.Encoding]::UTF8.GetBytes($Value))
        Invoke-Gcloud -ArgumentList @('secrets', 'versions', 'add', $Name, "--data-file=$file", '--project', $Environment.Project)
    }
    finally {
        Remove-Item -LiteralPath $file -Force
    }
}

function Test-KairoSecretHasValue {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    $versions = Invoke-Gcloud -ArgumentList @('secrets', 'versions', 'list', $Name, '--filter=state:ENABLED', '--format=value(name)', '--limit=1', '--project', $Environment.Project) -Capture
    [bool]($versions | Where-Object { $_ })
}

function Initialize-KairoServiceAccounts {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    foreach ($name in $script:ServiceAccounts) {
        if (-not (Test-Gcloud -ArgumentList (@('iam', 'service-accounts', 'describe', (Get-KairoServiceAccount -Environment $Environment -Name $name)) + $scope))) {
            Invoke-Gcloud -ArgumentList (@('iam', 'service-accounts', 'create', $name) + $scope)
        }
    }

    $accessor = @('--role', 'roles/secretmanager.secretAccessor')
    foreach ($name in 'kairo-api', 'kairo-worker', 'kairo-migrator') {
        Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $script:SqlSecret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)") + $accessor + $scope)
    }
    foreach ($secret in $script:GoogleSecrets.Values) {
        Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $secret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-api')") + $accessor + $scope)
    }

    $build = "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-build')"
    Add-KairoBinding -ArgumentList (@('artifacts', 'repositories', 'add-iam-policy-binding', $script:Registry, '--location', $Environment.Region, '--member', $build, '--role', 'roles/artifactregistry.writer') + $scope)
    Add-KairoBinding -ArgumentList (@('storage', 'buckets', 'add-iam-policy-binding', "gs://$($Environment.Project)-builds", '--member', $build, '--role', 'roles/storage.objectViewer') + $scope)
    Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', $build, '--role', 'roles/logging.logWriter', '--condition', 'None')
}

function Initialize-KairoKeys {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--location', $Environment.Region, '--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('kms', 'keyrings', 'describe', $script:KeyRing) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('kms', 'keyrings', 'create', $script:KeyRing) + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('kms', 'keys', 'describe', $script:TokenKey, '--keyring', $script:KeyRing) + $scope))) {
        $nextRotation = [DateTime]::UtcNow.AddDays(90).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
        Invoke-Gcloud -ArgumentList (@(
                'kms', 'keys', 'create', $script:TokenKey,
                '--keyring', $script:KeyRing,
                '--purpose', 'encryption',
                '--rotation-period', '90d',
                '--next-rotation-time', $nextRotation
            ) + $scope)
    }

    foreach ($name in 'kairo-api', 'kairo-worker') {
        Add-KairoBinding -ArgumentList (@(
                'kms', 'keys', 'add-iam-policy-binding', $script:TokenKey,
                '--keyring', $script:KeyRing,
                '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)",
                '--role', 'roles/cloudkms.cryptoKeyEncrypterDecrypter'
            ) + $scope)
    }
}

function Add-KairoBinding {
    param([Parameter(Mandatory)][string[]]$ArgumentList)

    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-Gcloud -ArgumentList $ArgumentList
            return
        }
        catch {
            if ($attempt -ge 5) {
                throw
            }

            Start-Sleep -Seconds 10
        }
    }
}

function New-KairoPassword {
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        do {
            $bytes = New-Object byte[] 32
            $random.GetBytes($bytes)
            $password = -join ($bytes | ForEach-Object { $script:PasswordCharacters[$_ % $script:PasswordCharacters.Length] })
        } until ($password -cmatch '[A-Z]' -and $password -cmatch '[a-z]' -and $password -match '[0-9]')
    }
    finally {
        $random.Dispose()
    }

    $password
}

function Set-KairoGoogleSignIn {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$ClientId,
        [Parameter(Mandatory)][string]$ClientSecret
    )

    Set-KairoSecretValue -Environment $Environment -Name $script:GoogleSecrets['Authentication__Google__ClientId'] -Value $ClientId.Trim()
    Set-KairoSecretValue -Environment $Environment -Name $script:GoogleSecrets['Authentication__Google__ClientSecret'] -Value $ClientSecret.Trim()
}
