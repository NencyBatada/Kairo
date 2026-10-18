function Get-KairoEvaluationTrust {
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
        [Parameter(Mandatory)][ValidatePattern('^\d+$')][string]$RepositoryId,
        [Parameter(Mandatory)][ValidatePattern('^\d+$')][string]$OwnerId,
        [ValidateRange(0, 2147483647)][int]$BootstrapPullRequest = 0
    )

    $protected = @('develop', 'main') | ForEach-Object {
        "assertion.workflow_ref=='$Repository/.github/workflows/ai-quality-live.yml@refs/heads/$_'"
    }
    $workflows = "(assertion.event_name in ['pull_request_target','workflow_dispatch'] && ($($protected -join ' || ')))"
    if ($BootstrapPullRequest) {
        $bootstrapRef = "refs/pull/$BootstrapPullRequest/merge"
        $workflows = "($workflows || (assertion.event_name=='pull_request' && assertion.ref=='$bootstrapRef' && assertion.workflow_ref=='$Repository/.github/workflows/ai-quality-bootstrap.yml@$bootstrapRef'))"
    }
    "assertion.repository_id=='$RepositoryId' && assertion.repository_owner_id=='$OwnerId' && $workflows"
}

function Initialize-KairoEvaluations {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$RepositoryId,
        [Parameter(Mandatory)][string]$OwnerId,
        [int]$BootstrapPullRequest = 0
    )

    $condition = Get-KairoEvaluationTrust -Repository $Repository -RepositoryId $RepositoryId -OwnerId $OwnerId -BootstrapPullRequest $BootstrapPullRequest
    $scope = @('--project', $Environment.Project)
    Invoke-Gcloud -ArgumentList (@('services', 'enable', 'aiplatform.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'sts.googleapis.com') + $scope)
    $projectNumber = (Invoke-Gcloud -ArgumentList (@('projects', 'describe', $Environment.Project, '--format', 'value(projectNumber)')) -Capture).Trim()
    $account = Get-KairoServiceAccount -Environment $Environment -Name 'kairo-evaluations'
    if (-not (Test-Gcloud -ArgumentList (@('iam', 'service-accounts', 'describe', $account) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('iam', 'service-accounts', 'create', 'kairo-evaluations', '--display-name', 'Kairo bounded AI evaluations') + $scope)
    }
    $role = 'kairoEvaluations'
    $verb = if (Test-Gcloud -ArgumentList (@('iam', 'roles', 'describe', $role) + $scope)) { 'update' } else { 'create' }
    Invoke-Gcloud -ArgumentList (@('iam', 'roles', $verb, $role, '--title', 'Kairo AI evaluation calls', '--stage', 'GA', '--permissions', 'aiplatform.endpoints.predict,aiplatform.locations.evaluateInstances,serviceusage.services.use') + $scope)
    Invoke-Gcloud -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', "serviceAccount:$account", '--role', "projects/$($Environment.Project)/roles/$role", '--condition=None')
    $pool = 'kairo-evaluations'
    $location = @('--location', 'global', '--workload-identity-pool', $pool)
    if (-not (Test-Gcloud -ArgumentList (@('iam', 'workload-identity-pools', 'describe', $pool, '--location', 'global') + $scope))) {
        Invoke-Gcloud -ArgumentList (@('iam', 'workload-identity-pools', 'create', $pool, '--location', 'global', '--display-name', 'Kairo trusted evaluations') + $scope)
    }
    $providerExists = Test-Gcloud -ArgumentList (@('iam', 'workload-identity-pools', 'providers', 'describe', 'github') + $location + $scope)
    $providerVerb = if ($providerExists) { 'update-oidc' } else { 'create-oidc' }
    Invoke-Gcloud -ArgumentList (@('iam', 'workload-identity-pools', 'providers', $providerVerb, 'github', '--issuer-uri', 'https://token.actions.githubusercontent.com', '--attribute-mapping', 'google.subject=assertion.sub,attribute.repository_id=assertion.repository_id,attribute.repository_owner_id=assertion.repository_owner_id', '--attribute-condition', $condition) + $location + $scope)
    $principal = "principalSet://iam.googleapis.com/projects/$projectNumber/locations/global/workloadIdentityPools/$pool/attribute.repository_id/$RepositoryId"
    Invoke-Gcloud -ArgumentList (@('iam', 'service-accounts', 'add-iam-policy-binding', $account, '--member', $principal, '--role', 'roles/iam.workloadIdentityUser') + $scope)
    [ordered]@{
        KAIRO_EVAL_PROJECT = $Environment.Project
        KAIRO_EVAL_SERVICE_ACCOUNT = $account
        KAIRO_EVAL_PROVIDER = "projects/$projectNumber/locations/global/workloadIdentityPools/$pool/providers/github"
    }
}
