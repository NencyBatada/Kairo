$script:Apis = @(
    'run.googleapis.com', 'sqladmin.googleapis.com', 'compute.googleapis.com', 'servicenetworking.googleapis.com',
    'artifactregistry.googleapis.com', 'cloudbuild.googleapis.com', 'secretmanager.googleapis.com',
    'iam.googleapis.com', 'storage.googleapis.com', 'cloudkms.googleapis.com',
    'googleads.googleapis.com', 'youtube.googleapis.com', 'merchantapi.googleapis.com', 'analyticsadmin.googleapis.com',
    'bigquery.googleapis.com', 'gmail.googleapis.com', 'analyticsdata.googleapis.com', 'cloudscheduler.googleapis.com',
    'aiplatform.googleapis.com', 'weather.googleapis.com', 'texttospeech.googleapis.com'
)
$script:ServiceAccounts = @('kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build', 'kairo-scheduler')
$script:FileBuckets = @('brand-files', 'catalogue-imports', 'generated-media')
$script:BucketLifecycles = @{
    'catalogue-imports' = 'catalogue-imports-lifecycle.json'
    'generated-media'   = 'generated-media-lifecycle.json'
}
$script:PasswordCharacters = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
$script:MailRedirectUri = 'http://localhost:8765/'
$script:GmailSendScope = 'https://www.googleapis.com/auth/gmail.send'

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
    Initialize-KairoBuckets -Environment $Environment
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

    foreach ($secret in @($script:GoogleSecrets.Values) + $script:MailSecret) {
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

function Get-KairoSecretValue {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    ((Invoke-Gcloud -ArgumentList @('secrets', 'versions', 'access', 'latest', "--secret=$Name", '--project', $Environment.Project) -Capture) -join "`n").Trim()
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
        foreach ($name in 'kairo-api', 'kairo-worker') {
            Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $secret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)") + $accessor + $scope)
        }
    }

    foreach ($name in 'kairo-api', 'kairo-worker') {
        foreach ($role in 'roles/bigquery.dataOwner', 'roles/bigquery.jobUser') {
            Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)", '--role', $role, '--condition', 'None')
        }
    }
    Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-agents')", '--role', 'roles/aiplatform.user', '--condition', 'None')
    Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $script:MailSecret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-api')") + $accessor + $scope)

    $build = "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-build')"
    Add-KairoBinding -ArgumentList (@('artifacts', 'repositories', 'add-iam-policy-binding', $script:Registry, '--location', $Environment.Region, '--member', $build, '--role', 'roles/artifactregistry.writer') + $scope)
    Add-KairoBinding -ArgumentList (@('storage', 'buckets', 'add-iam-policy-binding', "gs://$($Environment.Project)-builds", '--member', $build, '--role', 'roles/storage.objectViewer') + $scope)
    Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', $build, '--role', 'roles/logging.logWriter', '--condition', 'None')
}

function Initialize-KairoBuckets {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    foreach ($name in $script:FileBuckets) {
        $bucket = "gs://$($Environment.Project)-$name"
        if (-not (Test-Gcloud -ArgumentList (@('storage', 'buckets', 'describe', $bucket) + $scope))) {
            Invoke-Gcloud -ArgumentList (@('storage', 'buckets', 'create', $bucket, '--location', $Environment.Region, '--uniform-bucket-level-access', '--public-access-prevention') + $scope)
        }

        foreach ($service in 'kairo-api', 'kairo-worker') {
            Add-KairoBinding -ArgumentList (@('storage', 'buckets', 'add-iam-policy-binding', $bucket, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $service)", '--role', 'roles/storage.objectAdmin') + $scope)
        }

        if ($script:BucketLifecycles.ContainsKey($name)) {
            $rules = Join-Path $PSScriptRoot $script:BucketLifecycles[$name]
            Invoke-Gcloud -ArgumentList (@('storage', 'buckets', 'update', $bucket, "--lifecycle-file=$rules") + $scope)
        }
    }
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

function Set-KairoMailSender {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $sender = "$($Environment.MailSender)".Trim()
    if (-not $sender) {
        throw "Set MailSender for the $($Environment.Name) environment in deploy/environments.psd1 first."
    }

    $clientId = Get-KairoSecretValue -Environment $Environment -Name $script:GoogleSecrets['Authentication__Google__ClientId']
    $clientSecret = Get-KairoSecretValue -Environment $Environment -Name $script:GoogleSecrets['Authentication__Google__ClientSecret']
    $verifier = New-KairoOAuthVerifier
    $state = New-KairoOAuthVerifier
    $url = New-KairoMailConsentUrl -ClientId $clientId -Sender $sender -Verifier $verifier -State $state
    $response = Wait-KairoOAuthRedirect -Url $url -RedirectUri $script:MailRedirectUri
    if ($response.state -ne $state) {
        throw 'The sign-in response did not match this request. Run the script again.'
    }

    if ($response.error) {
        throw "Google sign-in failed: $($response.error)."
    }

    $grant = Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -Body @{
        grant_type    = 'authorization_code'
        code          = $response.code
        code_verifier = $verifier
        redirect_uri  = $script:MailRedirectUri
        client_id     = $clientId
        client_secret = $clientSecret
    }
    if (-not $grant.refresh_token) {
        throw 'Google returned no refresh token. Remove Kairo from the account''s third-party access, then run the script again.'
    }

    if (-not (("$($grant.scope)" -split ' ') -contains $script:GmailSendScope)) {
        throw 'Google did not grant permission to send email. Run the script again and allow Kairo to send email.'
    }

    $email = Get-KairoIdTokenEmail -IdToken $grant.id_token
    if ($email -ne $sender.ToLowerInvariant()) {
        throw "You signed in as $email, but MailSender is $sender. Run the script again and choose $sender."
    }

    Set-KairoSecretValue -Environment $Environment -Name $script:MailSecret -Value $grant.refresh_token
}

function New-KairoOAuthVerifier {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }

    ConvertTo-KairoBase64Url -Bytes $bytes
}

function ConvertTo-KairoBase64Url {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-KairoMailConsentUrl {
    param(
        [Parameter(Mandatory)][string]$ClientId,
        [Parameter(Mandatory)][string]$Sender,
        [Parameter(Mandatory)][string]$Verifier,
        [Parameter(Mandatory)][string]$State
    )

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $challenge = ConvertTo-KairoBase64Url -Bytes $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($Verifier))
    }
    finally {
        $sha.Dispose()
    }

    $query = [ordered]@{
        client_id             = $ClientId
        redirect_uri          = $script:MailRedirectUri
        response_type         = 'code'
        scope                 = "openid email $script:GmailSendScope"
        access_type           = 'offline'
        prompt                = 'consent'
        login_hint            = $Sender
        code_challenge        = $challenge
        code_challenge_method = 'S256'
        state                 = $State
    }
    'https://accounts.google.com/o/oauth2/v2/auth?' + (($query.GetEnumerator() | ForEach-Object { "$($_.Key)=$([Uri]::EscapeDataString($_.Value))" }) -join '&')
}

function Get-KairoIdTokenEmail {
    param([Parameter(Mandatory)][string]$IdToken)

    $parts = $IdToken.Split('.')
    if ($parts.Length -ne 3) {
        throw 'Google returned an ID token Kairo cannot read.'
    }

    $payload = $parts[1].Replace('-', '+').Replace('_', '/')
    $payload += '=' * ((4 - $payload.Length % 4) % 4)
    $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
    if (-not $claims.email_verified) {
        throw "Google has not verified the address $($claims.email)."
    }

    "$($claims.email)".ToLowerInvariant()
}

function Wait-KairoOAuthRedirect {
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$RedirectUri)

    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add($RedirectUri)
    $listener.Start()
    try {
        Write-Host "Opening your browser to sign in the sender mailbox. If it does not open, go to:`n$Url"
        Start-Process $Url
        $context = $listener.GetContext()
        $query = $context.Request.QueryString
        $result = @{ code = $query['code']; state = $query['state']; error = $query['error'] }
        $page = [Text.Encoding]::UTF8.GetBytes('<!DOCTYPE html><html><body style="font-family:sans-serif"><p>Kairo has what it needs. You can close this tab.</p></body></html>')
        $context.Response.ContentType = 'text/html; charset=utf-8'
        $context.Response.OutputStream.Write($page, 0, $page.Length)
        $context.Response.Close()
        $result
    }
    finally {
        $listener.Stop()
        $listener.Close()
    }
}
