BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{
        Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1'
        SqlTier = 'db-custom-1-3840'; SqlEdition = 'SQLSERVER_2022_EXPRESS'; SqlHighAvailability = $false
        MinInstances = @{ web = 0; api = 0 }
    }
    $script:instanceJson = '{"ipAddresses":[{"type":"PRIVATE","ipAddress":"10.20.0.3"}]}'
}

Describe 'Initialize-KairoProject' {
    BeforeEach {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:exists = $false
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $script:exists }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $line = $ArgumentList -join ' '
            $script:calls.Add($line)
            if ($line -like 'sql instances describe*') { $script:instanceJson }
            if ($line -like 'services vpc-peerings list*' -and $script:exists) { 'servicenetworking-googleapis-com' }
            if ($line -like 'secrets versions list*' -and $script:exists) { 'projects/1/secrets/kairo-sql-connection/versions/1' }
        }
        Mock -ModuleName Kairo.Deploy Set-KairoSecretValue { }
        Mock -ModuleName Kairo.Deploy Start-Sleep { }
    }

    Context 'in an empty project' {
        BeforeEach { Initialize-KairoProject -Environment $environment }

        It 'enables the APIs Kairo needs' {
            $calls | Should -Contain ('services enable run.googleapis.com sqladmin.googleapis.com compute.googleapis.com ' +
                'servicenetworking.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com ' +
                'secretmanager.googleapis.com iam.googleapis.com storage.googleapis.com --project kairo-test')
        }

        It 'creates the registry, the build bucket and the network' {
            $calls | Should -Contain 'artifacts repositories create kairo --repository-format docker --location asia-south1 --project kairo-test'
            $calls | Should -Contain 'storage buckets create gs://kairo-test-builds --location asia-south1 --uniform-bucket-level-access --project kairo-test'
            $calls | Should -Contain 'compute networks create kairo --subnet-mode custom --project kairo-test'
            $calls | Should -Contain 'compute networks subnets create kairo-run --network kairo --region asia-south1 --range 10.10.0.0/24 --project kairo-test'
            $calls | Should -Contain 'compute addresses create kairo-google-services --global --purpose VPC_PEERING --prefix-length 20 --network kairo --project kairo-test'
            $calls | Should -Contain 'services vpc-peerings connect --service servicenetworking.googleapis.com --ranges kairo-google-services --network kairo --project kairo-test'
        }

        It 'creates SQL Server Express with only a private address' {
            $create = $calls | Where-Object { $_ -like 'sql instances create kairo-sql *' }
            $create | Should -BeLike '*--database-version SQLSERVER_2022_EXPRESS*'
            $create | Should -BeLike '*--edition enterprise*'
            $create | Should -BeLike '*--tier db-custom-1-3840*'
            $create | Should -BeLike '*--network projects/kairo-test/global/networks/kairo*'
            $create | Should -BeLike '*--no-assign-ip*'
            $create | Should -BeLike '*--availability-type zonal*'
            $create | Should -BeLike '*--backup-start-time*'
            $create | Should -Match '--root-password=[A-Za-z0-9]{32}( |$)'
            $calls | Should -Contain 'sql databases create kairo --instance kairo-sql --project kairo-test'
        }

        It 'stores a connection string that uses the private address and the new password' {
            $password = [regex]::Match(($calls | Where-Object { $_ -like 'sql instances create*' }), '--root-password=(\S+)').Groups[1].Value
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -ParameterFilter {
                $Name -eq 'kairo-sql-connection' -and
                $Value -eq "Server=10.20.0.3;Database=kairo;User Id=sqlserver;Password=$password;Encrypt=True;TrustServerCertificate=True"
            }
        }

        It 'creates the Google secrets without a value' {
            $calls | Should -Contain 'secrets create google-client-id --replication-policy automatic --project kairo-test'
            $calls | Should -Contain 'secrets create google-client-secret --replication-policy automatic --project kairo-test'
        }

        It 'creates one service account per service' {
            foreach ($name in 'kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build') {
                $calls | Should -Contain "iam service-accounts create $name --project kairo-test"
            }
        }

        It 'lets each service read only the secrets it needs' {
            foreach ($name in 'kairo-api', 'kairo-worker', 'kairo-migrator') {
                $calls | Should -Contain "secrets add-iam-policy-binding kairo-sql-connection --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test"
            }
            foreach ($secret in 'google-client-id', 'google-client-secret') {
                $calls | Should -Contain "secrets add-iam-policy-binding $secret --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test"
            }
            ($calls | Where-Object { $_ -like 'secrets add-iam-policy-binding google-*kairo-web@*' }) | Should -BeNullOrEmpty
        }

        It 'lets Cloud Build read the source, push images and write logs' {
            $build = 'serviceAccount:kairo-build@kairo-test.iam.gserviceaccount.com'
            $calls | Should -Contain "artifacts repositories add-iam-policy-binding kairo --location asia-south1 --member $build --role roles/artifactregistry.writer --project kairo-test"
            $calls | Should -Contain "storage buckets add-iam-policy-binding gs://kairo-test-builds --member $build --role roles/storage.objectViewer --project kairo-test"
            $calls | Should -Contain "projects add-iam-policy-binding kairo-test --member $build --role roles/logging.logWriter --condition None"
        }
    }

    Context 'in a project that is already set up' {
        BeforeEach {
            $script:exists = $true
            Initialize-KairoProject -Environment $environment
        }

        It 'creates nothing and keeps the database password' {
            ($calls | Where-Object { $_ -match ' (create|connect|set-password) ' }) | Should -BeNullOrEmpty
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
        }
    }

    Context 'when the instance exists but its connection secret does not' {
        BeforeEach {
            Mock -ModuleName Kairo.Deploy Test-Gcloud { ($ArgumentList -join ' ') -notlike 'secrets describe kairo-sql-connection*' }
            Initialize-KairoProject -Environment $environment
        }

        It 'resets the password and stores a new connection string' {
            $reset = $calls | Where-Object { $_ -like 'sql users set-password sqlserver --instance kairo-sql *' }
            $reset | Should -Match '--password=[A-Za-z0-9]{32}( |$)'
            ($calls | Where-Object { $_ -like 'sql instances create*' }) | Should -BeNullOrEmpty
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -ParameterFilter { $Name -eq 'kairo-sql-connection' }
        }
    }

    Context 'when the connection secret exists but never got a value' {
        BeforeEach {
            Mock -ModuleName Kairo.Deploy Test-Gcloud { $true }
            Initialize-KairoProject -Environment $environment
        }

        It 'resets the password and stores a new connection string' {
            ($calls | Where-Object { $_ -like 'sql users set-password sqlserver --instance kairo-sql *' }) | Should -Not -BeNullOrEmpty
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -ParameterFilter { $Name -eq 'kairo-sql-connection' }
        }
    }

    Context 'with high availability' {
        It 'creates a regional instance' {
            $highlyAvailable = $environment.Clone()
            $highlyAvailable.SqlHighAvailability = $true

            Initialize-KairoProject -Environment $highlyAvailable

            ($calls | Where-Object { $_ -like 'sql instances create*' }) | Should -BeLike '*--availability-type regional*'
        }
    }
}

Describe 'New-KairoPassword' {
    It 'makes long, mixed, unambiguous passwords that differ each time' {
        $first = New-KairoPassword
        $second = New-KairoPassword

        $first | Should -Match '^[A-HJ-NP-Za-km-z2-9]{32}$'
        $first | Should -MatchExactly '[A-Z]'
        $first | Should -MatchExactly '[a-z]'
        $first | Should -Match '[0-9]'
        $second | Should -Not -Be $first
    }
}

Describe 'Set-KairoSecretValue' {
    It 'adds exactly the given value, with no trailing newline' {
        $script:written = $null
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $true }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $file = ($ArgumentList | Where-Object { $_ -like '--data-file=*' }) -replace '^--data-file=', ''
            $script:written = [IO.File]::ReadAllBytes($file)
        }

        Set-KairoSecretValue -Environment $environment -Name 'kairo-sql-connection' -Value 'Server=10.0.0.1;Password=x'

        [Text.Encoding]::UTF8.GetString($script:written) | Should -BeExactly 'Server=10.0.0.1;Password=x'
    }

    It 'creates the secret first when it is missing' {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $false }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { $script:calls.Add($ArgumentList -join ' ') }

        Set-KairoSecretValue -Environment $environment -Name 'kairo-sql-connection' -Value 'x'

        $calls[0] | Should -Be 'secrets create kairo-sql-connection --replication-policy automatic --project kairo-test'
        $calls[1] | Should -BeLike 'secrets versions add kairo-sql-connection --data-file=* --project kairo-test'
    }
}

Describe 'Test-KairoSecretHasValue' {
    It 'is true only when the secret has an enabled version' {
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { 'projects/1/secrets/google-client-id/versions/1' }
        Test-KairoSecretHasValue -Environment $environment -Name 'google-client-id' | Should -BeTrue

        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { }
        Test-KairoSecretHasValue -Environment $environment -Name 'google-client-id' | Should -BeFalse
    }
}

Describe 'Add-KairoBinding' {
    It 'retries while a new service account is not yet visible' {
        $script:attempts = 0
        Mock -ModuleName Kairo.Deploy Start-Sleep { }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $script:attempts++
            if ($script:attempts -lt 3) { throw 'Service account does not exist.' }
        }

        Add-KairoBinding -ArgumentList @('secrets', 'add-iam-policy-binding', 'x')

        $script:attempts | Should -Be 3
    }

    It 'gives up after five attempts' {
        Mock -ModuleName Kairo.Deploy Start-Sleep { }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { throw 'Service account does not exist.' }

        { Add-KairoBinding -ArgumentList @('secrets', 'add-iam-policy-binding', 'x') } | Should -Throw '*does not exist*'
        Should -Invoke -ModuleName Kairo.Deploy Invoke-Gcloud -Times 5 -Exactly
    }
}

Describe 'Set-KairoGoogleSignIn' {
    It 'stores the OAuth client ID and secret in their secrets' {
        Mock -ModuleName Kairo.Deploy Set-KairoSecretValue { }

        Set-KairoGoogleSignIn -Environment $environment -ClientId 'client-id.apps.googleusercontent.com' -ClientSecret 'GOCSPX-secret'

        Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -Exactly -ParameterFilter {
            $Name -eq 'google-client-id' -and $Value -eq 'client-id.apps.googleusercontent.com'
        }
        Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -Exactly -ParameterFilter {
            $Name -eq 'google-client-secret' -and $Value -eq 'GOCSPX-secret'
        }
    }

    It 'ignores spaces pasted around the values' {
        Mock -ModuleName Kairo.Deploy Set-KairoSecretValue { }

        Set-KairoGoogleSignIn -Environment $environment -ClientId " id `r`n" -ClientSecret "`tsecret "

        Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -ParameterFilter { $Name -eq 'google-client-id' -and $Value -ceq 'id' }
        Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -ParameterFilter { $Name -eq 'google-client-secret' -and $Value -ceq 'secret' }
    }
}
