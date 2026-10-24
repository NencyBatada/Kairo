BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{ Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1' }
    $script:shell = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
}

Describe 'Get-KairoAgentModels' {
    It 'uses the approved development model for both briefs and Guardian' {
        $models = Get-KairoAgentModels -Environment (Get-KairoEnvironment -Name development)
        $models.Count | Should -Be 2
        $models.KAIRO_PLAN_BRIEF_MODEL | Should -Be 'gemini-3.8-flash'
        $models.KAIRO_CHECK_MODEL | Should -Be 'gemini-3.8-flash'
    }

    It 'preserves environments without configured model overrides' {
        (Get-KairoAgentModels -Environment $environment).Count | Should -Be 0
    }

    It 'rejects settings that could inject workflow variables or deployment options' {
        { Get-KairoAgentModels -Environment @{ AgentModels = @{ OTHER = 'gemini-3.8-flash' } } } | Should -Throw
        { Get-KairoAgentModels -Environment @{ AgentModels = @{ KAIRO_CHECK_MODEL = "gemini-3.8-flash`nOTHER=unsafe" } } } | Should -Throw
        { Get-KairoAgentModels -Environment @{ AgentModels = @{ KAIRO_CHECK_MODEL = 'gemini-3.8-flash,OTHER=unsafe' } } } | Should -Throw
    }
}

Describe 'Get-KairoEnvironment' {
    It 'loads development from the environment file' {
        $environment = Get-KairoEnvironment -Name 'development'

        $environment.Name | Should -Be 'development'
        $environment.Project | Should -Be 'kairo2'
        $environment.Region | Should -Be 'asia-south1'
        $environment.ContainsKey('MailSender') | Should -BeTrue
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

    It 'derives the key that encrypts connection tokens' {
        Get-KairoTokenKeyName -Environment $environment |
            Should -Be 'projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens'
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

    It 'stops when the program is not installed, even when called from a prompt that carries on after errors' {
        $module = (Resolve-Path "$PSScriptRoot/../Kairo.Deploy").Path
        $command = "Import-Module '$module'; Invoke-Native -FilePath '$shell' -ArgumentList @('-NoProfile', '-Command', 'exit 0'); " +
            "Invoke-Native -FilePath 'kairo-no-such-tool' -ArgumentList @('--version'); 'carried on'"

        $output = & $shell -NoProfile -Command $command 2>&1 | ForEach-Object { "$_" }

        $output | Should -Not -Contain 'carried on'
        ($output -join ' ') | Should -BeLike '*kairo-no-such-tool*'
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
            $FilePath -eq 'gcloud' -and ($ArgumentList -join ' ') -eq '--quiet run services list'
        }
    }
}

Describe 'Test-Native' {
    BeforeAll {
        $script:countArguments = Join-Path ([IO.Path]::GetTempPath()) 'kairo-count-arguments.ps1'
        Set-Content -LiteralPath $countArguments -Value 'if ($args.Count -eq 3) { exit 0 } else { exit 1 }'
    }

    AfterAll {
        Remove-Item -LiteralPath $countArguments -Force
    }

    It 'passes each argument separately to a PowerShell script, as gcloud is on Windows' {
        Test-Native -FilePath $countArguments -ArgumentList @('artifacts', 'repositories', 'describe') | Should -BeTrue
    }

    It 'is false when the program fails' {
        Test-Native -FilePath $countArguments -ArgumentList @('only-one') | Should -BeFalse
    }

    It 'passes each argument separately when running a program' {
        { Invoke-Native -FilePath $countArguments -ArgumentList @('artifacts', 'repositories', 'describe') } | Should -Not -Throw
    }
}

Describe 'Test-Gcloud' {
    It 'adds --quiet' {
        Mock -ModuleName Kairo.Deploy Test-Native { $true }

        Test-Gcloud -ArgumentList @('secrets', 'describe', 'x') | Should -BeTrue

        Should -Invoke -ModuleName Kairo.Deploy Test-Native -ParameterFilter {
            $FilePath -eq 'gcloud' -and ($ArgumentList -join ' ') -eq 'secrets describe x --quiet'
        }
    }
}

Describe 'Test-Native when the program is missing' {
    It 'is false' {
        Test-Native -FilePath 'kairo-no-such-tool' -ArgumentList @('describe') | Should -BeFalse
    }
}

Describe 'Campaign generation limits' {
    It 'publishes campaign defaults for every environment' {
        $settings = Get-KairoGenerationSettings -Environment $environment
        $settings | Should -Be 'Kairo__Operations__Limits__TextCalls=100,Kairo__Operations__Limits__Images=50,Kairo__Operations__Limits__VideoClips=10'
    }

    It 'accepts configured caps including zero' {
        (Get-KairoGenerationSettings -Environment @{ GenerationLimits = @{ TextCalls = 0; Images = 7; VideoClips = 2 } }) |
            Should -Be 'Kairo__Operations__Limits__TextCalls=0,Kairo__Operations__Limits__Images=7,Kairo__Operations__Limits__VideoClips=2'
    }

    It 'rejects unknown fields and unsafe or negative values' {
        { Get-KairoGenerationSettings -Environment @{ GenerationLimits = @{ Images = -1 } } } | Should -Throw
        { Get-KairoGenerationSettings -Environment @{ GenerationLimits = @{ Images = '2,OTHER=1' } } } | Should -Throw
        { Get-KairoGenerationSettings -Environment @{ GenerationLimits = @{ OTHER = 1 } } } | Should -Throw
    }
}
