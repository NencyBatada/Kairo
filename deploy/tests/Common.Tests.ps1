BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{ Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1' }
    $script:shell = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
}

Describe 'Get-KairoEnvironment' {
    It 'loads development from the environment file' {
        $environment = Get-KairoEnvironment -Name 'development'

        $environment.Name | Should -Be 'development'
        $environment.Project | Should -Be 'kairo2'
        $environment.Region | Should -Be 'asia-south1'
    }

    It 'refuses an unknown environment' {
        { Get-KairoEnvironment -Name 'qa' } | Should -Throw "Unknown environment 'qa'*"
    }

    It 'refuses an environment without a project' {
        { Get-KairoEnvironment -Name 'staging' } | Should -Throw '*staging environment has no project yet*'
    }
}

Describe 'names' {
    It 'derives a Cloud Run service URL' {
        Get-KairoServiceUrl -Environment $environment -ProjectNumber '123' -Service 'kairo-api' |
            Should -Be 'https://kairo-api-123.asia-south1.run.app'
    }

    It 'derives an image reference' {
        Get-KairoImage -Environment $environment -Name 'api' -Tag 'abc123' |
            Should -Be 'asia-south1-docker.pkg.dev/kairo-test/kairo/api:abc123'
    }

    It 'derives a service account' {
        Get-KairoServiceAccount -Environment $environment -Name 'kairo-web' |
            Should -Be 'kairo-web@kairo-test.iam.gserviceaccount.com'
    }
}

Describe 'Hide-KairoSecrets' {
    It 'hides passwords' {
        Hide-KairoSecrets -Text 'gcloud sql instances create --root-password=s3cret --tier x' |
            Should -Be 'gcloud sql instances create --root-password=*** --tier x'
        Hide-KairoSecrets -Text 'gcloud sql users set-password sqlserver --password=s3cret' |
            Should -Be 'gcloud sql users set-password sqlserver --password=***'
    }
}

Describe 'Invoke-Native' {
    It 'returns the output when asked to capture it' {
        Invoke-Native -FilePath $shell -ArgumentList @('-NoProfile', '-Command', 'Write-Output hello') -Capture |
            Should -Be 'hello'
    }

    It 'throws with the exit code when the program fails' {
        { Invoke-Native -FilePath $shell -ArgumentList @('-NoProfile', '-Command', 'exit 3') } |
            Should -Throw '*failed with exit code 3*'
    }

    It 'keeps passwords out of the failure message' {
        $message = $null
        try {
            Invoke-Native -FilePath $shell -ArgumentList @('-NoProfile', '-Command', 'exit 3', '--root-password=s3cret')
        }
        catch {
            $message = $_.Exception.Message
        }

        $message | Should -Not -BeNullOrEmpty
        $message | Should -Not -BeLike '*s3cret*'
    }
}

Describe 'Invoke-Gcloud' {
    It 'adds --quiet so gcloud never waits for an answer' {
        Mock -ModuleName Kairo.Deploy Invoke-Native { }

        Invoke-Gcloud -ArgumentList @('run', 'services', 'list')

        Should -Invoke -ModuleName Kairo.Deploy Invoke-Native -ParameterFilter {
            $FilePath -eq 'gcloud' -and ($ArgumentList -join ' ') -eq 'run services list --quiet'
        }
    }
}
