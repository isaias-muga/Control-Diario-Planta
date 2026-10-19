<#
.SYNOPSIS
  Creates (or completes) the production instance of Control Diario Fabrica in Azure. Safe to run again: every step checks what already exists.

.DESCRIPTION
  Everything goes in the resource group rg-control-diario-prod (Brazil South). It never touches rg-dairycontrol (the demo).
  Read docs/production.md first. Run it yourself, from the repository root, in Windows PowerShell:

      .\infra\production.ps1 -DryRun     # prints what it would create, changes nothing
      .\infra\production.ps1             # does it

  Secrets: the SQL password, the JWT signing key and the PIN secret are generated here, in memory, and go straight to the Container Apps
  secrets. They are never printed, logged or written to a file. The only value you type is the bootstrap admin password (not echoed).
  Do not run this script under Start-Transcript or any tool that records the console.

.PARAMETER DryRun
  Runs only the read-only commands (show, list, get) and prints every command that would create or change something.

.PARAMETER DeployRoleScope
  Apps (default): the GitHub identity is Contributor on the two container apps only (plus AcrPush on the registry and Reader on the
  group). ResourceGroup: Contributor on the whole group; use it if the first deploy fails with an authorization error.

.PARAMETER BackupRedundancy
  Where the database backups live. Zone (default) keeps them in Brazil South across zones; use Local if Azure refuses Zone.

.PARAMETER GitHubRepo
  owner/name of the repository that deploys.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [ValidateSet('Apps', 'ResourceGroup')][string]$DeployRoleScope = 'Apps',
    [ValidateSet('Zone', 'Local', 'Geo')][string]$BackupRedundancy = 'Zone',
    [string]$GitHubRepo = 'isaias-muga/Control-Diario-Planta'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------------------------
# Names (nothing here is secret)
# ---------------------------------------------------------------------------------------------------------------------------------
$ResourceGroup = 'rg-control-diario-prod'
$Location = 'brazilsouth'
$DemoResourceGroup = 'rg-dairycontrol'
$WorkspaceName = 'log-cdf-prod'
$EnvironmentName = 'cae-cdf-prod'
$PullIdentityName = 'id-cdf-pull'
$ApiApp = 'cdf-api'
$ClientApp = 'cdf-client'
$DeployAppName = 'github-cdf-prod-deploy'
$FederatedCredentialName = 'github-main'
$BudgetName = 'bud-cdf-prod'
$BudgetAmountUsd = 25
$SqlAdminUser = 'cdfadmin'
$DatabaseName = 'ControlDiario'
$PlaceholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'
$PlaceholderPort = 80 # the real port (8080) is set by the first deploy, once a real image runs
$ProvidersNeeded = @('Microsoft.App', 'Microsoft.OperationalInsights', 'Microsoft.ContainerRegistry', 'Microsoft.Sql', 'Microsoft.ManagedIdentity', 'Microsoft.Consumption')

if ($ResourceGroup -eq $DemoResourceGroup) { throw 'Refusing to run: the production group must never be the demo group.' }

# ---------------------------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------------------------
$script:Secrets = New-Object System.Collections.Generic.List[string] # every secret value, to mask them in any message

# A value that must never be printed: Prefix is part of the argument sent to az (for example "jwt-key="), Shown is what a dry run prints
function New-Secret([string]$Value, [string]$Shown, [string]$Prefix = '') {
    if ($Value) { $script:Secrets.Add($Value) }
    return @{ Secret = ($Prefix + $Value); Shown = $Shown }
}

function Hide-Secrets([string]$Text) {
    foreach ($s in $script:Secrets) { if ($s) { $Text = $Text.Replace($s, '***') } }
    return $Text
}

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "   $Text" }

# Runs az / gh. -ReadOnly commands always run; anything else is only printed with -DryRun. Returns @{ ExitCode; Output }.
function Invoke-Cli {
    param(
        [Parameter(Mandatory)][string]$Exe,
        [Parameter(Mandatory)][object[]]$CliArgs,
        [switch]$ReadOnly,
        [switch]$AllowFail
    )
    $real = @(); $shown = @()
    foreach ($a in $CliArgs) {
        if ($a -is [hashtable]) { $real += [string]$a.Secret; $shown += $a.Shown } else { $real += [string]$a; $shown += [string]$a }
    }
    if ($Exe -eq 'az') { $real += '--only-show-errors'; $shown += '--only-show-errors' }

    if ($DryRun -and -not $ReadOnly) {
        Write-Host "   [dry run] $Exe $($shown -join ' ')" -ForegroundColor DarkYellow
        return @{ ExitCode = 0; Output = '' }
    }

    # Windows PowerShell 5.1 turns native stderr into terminating errors; look at the exit code instead
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $Exe @real 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    $text = Hide-Secrets (($lines | Out-String).Trim())

    if ($code -ne 0 -and -not $AllowFail) {
        throw "Command failed (exit $code): $Exe $($shown -join ' ')`n$text"
    }
    return @{ ExitCode = $code; Output = $text }
}

function Invoke-Az { param([object[]]$CliArgs, [switch]$ReadOnly, [switch]$AllowFail) Invoke-Cli -Exe 'az' -CliArgs $CliArgs -ReadOnly:$ReadOnly -AllowFail:$AllowFail }
function Invoke-Gh { param([object[]]$CliArgs, [switch]$ReadOnly, [switch]$AllowFail) Invoke-Cli -Exe 'gh' -CliArgs $CliArgs -ReadOnly:$ReadOnly -AllowFail:$AllowFail }

# Read-only query that returns the text of the answer (empty when the thing does not exist)
function Get-AzValue([object[]]$CliArgs) {
    $r = Invoke-Az -CliArgs $CliArgs -ReadOnly -AllowFail
    if ($r.ExitCode -ne 0 -or $r.Output -eq 'None') { return '' }
    return $r.Output
}

# The subject of the federated credential called $Name on the app registration, or '' when there is none. It asks az for that one
# credential only (show by name, tsv), so no JSON list is parsed: in Windows PowerShell 5.1 ConvertFrom-Json '[]' is not enumerated and
# reading a property of it fails under strict mode (this is what stopped the first run).
function Get-FederatedCredentialSubject([string]$AppId, [string]$Name) {
    $text = Get-AzValue @('ad', 'app', 'federated-credential', 'show', '--id', $AppId, '--federated-credential-id', $Name, '--query', 'subject', '--output', 'tsv')
    if (-not $text) { return '' }
    return (@($text -split "`r?`n") | Select-Object -First 1).Trim()
}

function Test-AzExists([object[]]$CliArgs) {
    return ((Invoke-Az -CliArgs ($CliArgs + @('--output', 'none')) -ReadOnly -AllowFail).ExitCode -eq 0)
}

function New-RandomBytes([int]$Count) {
    $bytes = New-Object byte[] $Count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return , $bytes
}

function New-RandomHex([int]$Bytes) { return (-join ((New-RandomBytes $Bytes) | ForEach-Object { $_.ToString('x2') })) }

# 32 letters and digits with at least one lower case, one upper case and one digit (SQL complexity rules; nothing that needs escaping)
function New-SqlPassword {
    $alphabet = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    do {
        $bytes = New-RandomBytes 32
        $candidate = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
    } until ($candidate -cmatch '[a-z]' -and $candidate -cmatch '[A-Z]' -and $candidate -match '[0-9]')
    return $candidate
}

function ConvertTo-PlainText([System.Security.SecureString]$Secure) {
    $ptr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function Read-AdminPassword {
    while ($true) {
        $first = ConvertTo-PlainText (Read-Host 'Bootstrap admin password (at least 12 characters; you will be forced to change it at the first login)' -AsSecureString)
        $second = ConvertTo-PlainText (Read-Host 'Type it again' -AsSecureString)
        if ($first -cne $second) { Write-Host 'They do not match. Try again.' -ForegroundColor Yellow; continue }
        if ($first.Length -lt 12) { Write-Host 'It needs at least 12 characters. Try again.' -ForegroundColor Yellow; continue }
        # On Windows az is a .cmd file: characters like & | < > ^ % " ! ' ; or a space would be read by cmd.exe and could cut or change the secret
        if ($first -notmatch '^[A-Za-z0-9._@#+=-]+$') { Write-Host 'Use only letters, digits and . _ @ # + = - (other symbols can be mangled on the way to Azure). Try again.' -ForegroundColor Yellow; continue }
        return $first
    }
}

function Write-TempJson([object]$Data) {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("cdf-" + [guid]::NewGuid().ToString('N') + '.json')
    [System.IO.File]::WriteAllText($path, ($Data | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
    return $path
}

function Invoke-WithRetry([scriptblock]$Action, [int]$Attempts = 4, [int]$DelaySeconds = 20) {
    for ($i = 1; $i -le $Attempts; $i++) {
        try { return & $Action }
        catch {
            if ($i -eq $Attempts) { throw }
            Write-Info "Not ready yet (attempt $i of $Attempts); waiting $DelaySeconds s. Azure needs a moment to spread new identities and roles."
            if (-not $DryRun) { Start-Sleep -Seconds $DelaySeconds }
        }
    }
}

function Confirm-Role([string]$PrincipalId, [string]$Role, [string]$Scope) {
    $count = Get-AzValue @('role', 'assignment', 'list', '--assignee', $PrincipalId, '--role', $Role, '--scope', $Scope, '--query', 'length(@)', '--output', 'tsv')
    if ($count -and [int]$count -gt 0) { Write-Info "Role $Role already assigned on $Scope."; return }
    Write-Info "Assigning $Role on $Scope."
    Invoke-WithRetry { Invoke-Az -CliArgs @('role', 'assignment', 'create', '--assignee-object-id', $PrincipalId, '--assignee-principal-type', 'ServicePrincipal', '--role', $Role, '--scope', $Scope) | Out-Null }
}

function Remove-RoleIfPresent([string]$PrincipalId, [string]$Role, [string]$Scope) {
    $count = Get-AzValue @('role', 'assignment', 'list', '--assignee', $PrincipalId, '--role', $Role, '--scope', $Scope, '--query', 'length(@)', '--output', 'tsv')
    if ($count -and [int]$count -gt 0) {
        Write-Info "Removing $Role on $Scope (it belongs to the other -DeployRoleScope)."
        Invoke-Az -CliArgs @('role', 'assignment', 'delete', '--assignee', $PrincipalId, '--role', $Role, '--scope', $Scope) | Out-Null
    }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Preflight (read-only)
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step 'Checking the tools and the subscription'
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'The Azure CLI is not installed. See docs/production.md (How to run).' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is not installed. See docs/production.md (How to run).' }

$accountRaw = Invoke-Az -CliArgs @('account', 'show', '--output', 'json') -ReadOnly -AllowFail
if ($accountRaw.ExitCode -ne 0) { throw 'You are not logged in to Azure. Run: az login' }
$account = $accountRaw.Output | ConvertFrom-Json
$subscriptionId = $account.id
$tenantId = $account.tenantId

$subscriptionRaw = Invoke-Az -CliArgs @('rest', '--method', 'get', '--url', "https://management.azure.com/subscriptions/$subscriptionId`?api-version=2022-12-01", '--output', 'json') -ReadOnly
$policies = ($subscriptionRaw.Output | ConvertFrom-Json).subscriptionPolicies
$quotaId = [string]$policies.quotaId
$spendingLimit = [string]$policies.spendingLimit
Write-Info "Subscription: $($account.name) ($subscriptionId)"
Write-Info "Offer: $quotaId, spending limit: $spendingLimit"
if ($quotaId -like 'FreeTrial*' -or $spendingLimit -eq 'On') {
    throw 'This subscription is still a free trial (or has a spending limit). Upgrade it to pay-as-you-go in the Azure portal (Subscriptions, Upgrade), then run the script again.'
}
if ($quotaId -notlike 'PayAsYouGo*') {
    throw "The offer of this subscription is '$quotaId', not pay-as-you-go. This script is meant for a pay-as-you-go subscription; check docs/production.md before going on."
}

$ghAuth = Invoke-Gh -CliArgs @('auth', 'status') -ReadOnly -AllowFail
if ($ghAuth.ExitCode -ne 0) { throw 'You are not logged in to GitHub. Run: gh auth login' }
$repoInfo = (Invoke-Gh -CliArgs @('api', "repos/$GitHubRepo") -ReadOnly).Output | ConvertFrom-Json
$ownerName = $repoInfo.owner.login
$ownerId = $repoInfo.owner.id
$repoName = $repoInfo.name
$repoId = $repoInfo.id
$federatedSubject = "repo:$ownerName@$ownerId/$repoName@$repoId`:ref:refs/heads/main"
Write-Info "Repository: $ownerName/$repoName (private: $($repoInfo.private))"

$budgetEmail = [string]$account.user.name
if ($budgetEmail -notmatch '@') { throw 'Could not read your e-mail from az account show; the budget alert needs it.' }

$hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($subscriptionId))
$suffix = (-join ($hash | Select-Object -First 3 | ForEach-Object { $_.ToString('x2') }))
$AcrName = "crcontroldiario$suffix"
$SqlServer = "sql-cdf-prod-$suffix"
$AcrServer = "$AcrName.azurecr.io"
$GroupScope = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup"

Write-Step 'What this will do'
Write-Info "Subscription : $($account.name)"
Write-Info "Group        : $ResourceGroup in $Location (the demo group $DemoResourceGroup is not touched)"
Write-Info "Resources    : registry $AcrName, SQL server $SqlServer (Basic, TLS 1.2, locked), apps $ApiApp and $ClientApp (1 replica each),"
Write-Info "               workspace $WorkspaceName (0.1 GB/day cap), budget of USD $BudgetAmountUsd to $budgetEmail"
Write-Info "GitHub       : federated credential for $federatedSubject, secrets AZURE_*, variable ACR_NAME on $GitHubRepo"
Write-Info "Deploy roles : $DeployRoleScope"
if ($DryRun) {
    Write-Host '   DRY RUN: nothing will be created or changed.' -ForegroundColor Yellow
}
else {
    $answer = Read-Host 'Type SI (capital letters) to create these resources in this subscription'
    if ($answer -cne 'SI') { throw 'Cancelled. Nothing was changed.' }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Providers, resource group
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step 'Azure CLI extension and resource providers'
Invoke-Az -CliArgs @('extension', 'add', '--name', 'containerapp', '--upgrade') | Out-Null
foreach ($provider in $ProvidersNeeded) {
    $state = Get-AzValue @('provider', 'show', '--namespace', $provider, '--query', 'registrationState', '--output', 'tsv')
    if ($state -eq 'Registered') { Write-Info "$provider already registered."; continue }
    Write-Info "Registering $provider (can take a minute)."
    Invoke-Az -CliArgs @('provider', 'register', '--namespace', $provider, '--wait') | Out-Null
}

Write-Step "Resource group $ResourceGroup"
$existingLocation = Get-AzValue @('group', 'show', '--name', $ResourceGroup, '--query', 'location', '--output', 'tsv')
if ($existingLocation) {
    if ($existingLocation -ne $Location) { throw "The group $ResourceGroup exists but in $existingLocation, not $Location. Stopping." }
    Write-Info 'Already exists.'
}
else {
    Invoke-Az -CliArgs @('group', 'create', '--name', $ResourceGroup, '--location', $Location) | Out-Null
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Registry, logs, pull identity
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step "Container registry $AcrName (Basic, no admin user)"
if (Test-AzExists @('acr', 'show', '--name', $AcrName, '--resource-group', $ResourceGroup)) { Write-Info 'Already exists.' }
else {
    $free = Get-AzValue @('acr', 'check-name', '--name', $AcrName, '--query', 'nameAvailable', '--output', 'tsv')
    if ($free -eq 'false') { throw "The registry name $AcrName is taken by someone else. Change `$AcrName in the script." }
    Invoke-Az -CliArgs @('acr', 'create', '--name', $AcrName, '--resource-group', $ResourceGroup, '--location', $Location, '--sku', 'Basic', '--admin-enabled', 'false') | Out-Null
}
$acrId = Get-AzValue @('acr', 'show', '--name', $AcrName, '--resource-group', $ResourceGroup, '--query', 'id', '--output', 'tsv')
if (-not $acrId) { $acrId = "$GroupScope/providers/Microsoft.ContainerRegistry/registries/$AcrName" }

Write-Step "Log Analytics workspace $WorkspaceName (cap 0.1 GB a day, 30 days of logs)"
if (Test-AzExists @('monitor', 'log-analytics', 'workspace', 'show', '--resource-group', $ResourceGroup, '--workspace-name', $WorkspaceName)) {
    Write-Info 'Already exists; making sure the daily cap is set.'
    Invoke-Az -CliArgs @('monitor', 'log-analytics', 'workspace', 'update', '--resource-group', $ResourceGroup, '--workspace-name', $WorkspaceName, '--quota', '0.1') | Out-Null
}
else {
    Invoke-Az -CliArgs @('monitor', 'log-analytics', 'workspace', 'create', '--resource-group', $ResourceGroup, '--workspace-name', $WorkspaceName, '--location', $Location, '--quota', '0.1', '--retention-time', '30') | Out-Null
}
$workspaceCustomerId = Get-AzValue @('monitor', 'log-analytics', 'workspace', 'show', '--resource-group', $ResourceGroup, '--workspace-name', $WorkspaceName, '--query', 'customerId', '--output', 'tsv')

Write-Step "Managed identity $PullIdentityName (pulls images from the registry)"
$identityNew = $false
if (Test-AzExists @('identity', 'show', '--name', $PullIdentityName, '--resource-group', $ResourceGroup)) { Write-Info 'Already exists.' }
else {
    Invoke-Az -CliArgs @('identity', 'create', '--name', $PullIdentityName, '--resource-group', $ResourceGroup, '--location', $Location) | Out-Null
    $identityNew = $true
}
$identityPrincipalId = Get-AzValue @('identity', 'show', '--name', $PullIdentityName, '--resource-group', $ResourceGroup, '--query', 'principalId', '--output', 'tsv')
$identityResourceId = Get-AzValue @('identity', 'show', '--name', $PullIdentityName, '--resource-group', $ResourceGroup, '--query', 'id', '--output', 'tsv')
if (-not $identityPrincipalId) { $identityPrincipalId = '<identity-principal-id>'; $identityResourceId = "$GroupScope/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$PullIdentityName" }
Confirm-Role -PrincipalId $identityPrincipalId -Role 'AcrPull' -Scope $acrId
if ($identityNew -and -not $DryRun) { Write-Info 'Waiting 30 s for the new role to spread.'; Start-Sleep -Seconds 30 }

# ---------------------------------------------------------------------------------------------------------------------------------
# Container Apps environment
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step "Container Apps environment $EnvironmentName"
if (Test-AzExists @('containerapp', 'env', 'show', '--name', $EnvironmentName, '--resource-group', $ResourceGroup)) { Write-Info 'Already exists.' }
else {
    $workspaceKey = Get-AzValue @('monitor', 'log-analytics', 'workspace', 'get-shared-keys', '--resource-group', $ResourceGroup, '--workspace-name', $WorkspaceName, '--query', 'primarySharedKey', '--output', 'tsv')
    if (-not $workspaceKey) { $workspaceKey = 'dry-run-placeholder' }
    Invoke-Az -CliArgs @('containerapp', 'env', 'create', '--name', $EnvironmentName, '--resource-group', $ResourceGroup, '--location', $Location,
        '--logs-workspace-id', $workspaceCustomerId, '--logs-workspace-key', (New-Secret $workspaceKey '***')) | Out-Null
    $workspaceKey = $null
}
$defaultDomain = Get-AzValue @('containerapp', 'env', 'show', '--name', $EnvironmentName, '--resource-group', $ResourceGroup, '--query', 'properties.defaultDomain', '--output', 'tsv')
if (-not $defaultDomain) { $defaultDomain = '<environment-default-domain>' }

# ---------------------------------------------------------------------------------------------------------------------------------
# The apps decide whether secrets are generated: with the apps in place nothing is rotated
# ---------------------------------------------------------------------------------------------------------------------------------
$apiExists = Test-AzExists @('containerapp', 'show', '--name', $ApiApp, '--resource-group', $ResourceGroup)
$clientExists = Test-AzExists @('containerapp', 'show', '--name', $ClientApp, '--resource-group', $ResourceGroup)
$sqlPassword = $null
if (-not $apiExists) {
    $sqlPassword = New-Secret (New-SqlPassword) '***'
}

Write-Step "SQL server $SqlServer and database $DatabaseName (Basic, no auto-pause, 7-day point-in-time restore, TLS 1.2)"
$serverExists = Test-AzExists @('sql', 'server', 'show', '--name', $SqlServer, '--resource-group', $ResourceGroup)
if (-not $serverExists) {
    if ($apiExists) { Write-Warning "$ApiApp exists but the SQL server did not: its connection string secret points at the old server and must be replaced (see docs/production.md)." }
    Invoke-Az -CliArgs @('sql', 'server', 'create', '--name', $SqlServer, '--resource-group', $ResourceGroup, '--location', $Location,
        '--admin-user', $SqlAdminUser, '--admin-password', $(if ($sqlPassword) { $sqlPassword } else { New-Secret (New-SqlPassword) '***' }),
        '--minimal-tls-version', '1.2') | Out-Null
}
elseif ($sqlPassword) {
    Write-Info 'The server exists but the API app does not (an interrupted run): setting a new SQL password for the new connection string.'
    Invoke-Az -CliArgs @('sql', 'server', 'update', '--name', $SqlServer, '--resource-group', $ResourceGroup, '--admin-password', $sqlPassword, '--minimal-tls-version', '1.2') | Out-Null
}
else {
    Write-Info 'Server already exists; making sure TLS 1.2 is the minimum.'
    Invoke-Az -CliArgs @('sql', 'server', 'update', '--name', $SqlServer, '--resource-group', $ResourceGroup, '--minimal-tls-version', '1.2') | Out-Null
}

if (Test-AzExists @('sql', 'server', 'firewall-rule', 'show', '--server', $SqlServer, '--resource-group', $ResourceGroup, '--name', 'AllowAzureServices')) { Write-Info 'Firewall rule for Azure services already exists.' }
else {
    # Container Apps without a virtual network have no fixed address, so the "Azure services" rule is how the API reaches the database
    Invoke-Az -CliArgs @('sql', 'server', 'firewall-rule', 'create', '--server', $SqlServer, '--resource-group', $ResourceGroup, '--name', 'AllowAzureServices', '--start-ip-address', '0.0.0.0', '--end-ip-address', '0.0.0.0') | Out-Null
}

if (Test-AzExists @('sql', 'db', 'show', '--server', $SqlServer, '--resource-group', $ResourceGroup, '--name', $DatabaseName)) { Write-Info 'Database already exists.' }
else {
    try {
        Invoke-Az -CliArgs @('sql', 'db', 'create', '--server', $SqlServer, '--resource-group', $ResourceGroup, '--name', $DatabaseName,
            '--edition', 'Basic', '--service-objective', 'Basic', '--backup-storage-redundancy', $BackupRedundancy) | Out-Null
    }
    catch {
        Write-Host "The database could not be created with backup redundancy '$BackupRedundancy'. If the message above says it is not supported here, run again with -BackupRedundancy Local." -ForegroundColor Yellow
        throw
    }
}
Invoke-Az -CliArgs @('sql', 'db', 'str-policy', 'set', '--server', $SqlServer, '--resource-group', $ResourceGroup, '--name', $DatabaseName, '--retention-days', '7') | Out-Null
$sqlFqdn = Get-AzValue @('sql', 'server', 'show', '--name', $SqlServer, '--resource-group', $ResourceGroup, '--query', 'fullyQualifiedDomainName', '--output', 'tsv')
if (-not $sqlFqdn) { $sqlFqdn = "$SqlServer.database.windows.net" }

# ---------------------------------------------------------------------------------------------------------------------------------
# The two container apps (placeholder image: the first deploy puts the real images and the probes)
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step "Container apps $ApiApp (internal) and $ClientApp (external)"
if ($apiExists) {
    Write-Info "$ApiApp already exists: its secrets are left as they are."
}
else {
    $adminPassword = $null
    if (-not $DryRun) { $adminPassword = Read-AdminPassword } else { $adminPassword = 'dry-run-placeholder' }
    $connection = "Server=tcp:$sqlFqdn,1433;Initial Catalog=$DatabaseName;Persist Security Info=False;User ID=$SqlAdminUser;Password=$($sqlPassword.Secret);MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60"
    $jwtKey = New-RandomHex 32
    $pinSecret = New-RandomHex 32
    $sConnection = New-Secret $connection 'db-connection=***' 'db-connection='
    $sJwt = New-Secret $jwtKey 'jwt-key=***' 'jwt-key='
    $sPin = New-Secret $pinSecret 'pin-secret=***' 'pin-secret='
    $sAdmin = New-Secret $adminPassword 'admin-password=***' 'admin-password='

    Invoke-WithRetry {
        # A retry after a call that half worked must not fail on "already exists"
        if (Test-AzExists @('containerapp', 'show', '--name', $ApiApp, '--resource-group', $ResourceGroup)) { return }
        Invoke-Az -CliArgs @('containerapp', 'create', '--name', $ApiApp, '--resource-group', $ResourceGroup, '--environment', $EnvironmentName,
            '--image', $PlaceholderImage, '--ingress', 'internal', '--target-port', $PlaceholderPort,
            '--min-replicas', '1', '--max-replicas', '1', '--cpu', '0.25', '--memory', '0.5Gi',
            '--user-assigned', $identityResourceId, '--registry-server', $AcrServer, '--registry-identity', $identityResourceId,
            '--secrets', $sConnection, $sJwt, $sPin, $sAdmin,
            '--env-vars', 'ConnectionStrings__Default=secretref:db-connection', 'Jwt__SigningKey=secretref:jwt-key', 'Pin__Secret=secretref:pin-secret',
            'AdminUser__Password=secretref:admin-password', 'Database__ApplyMigrationsOnStartup=true', 'DemoData__SeedOnStartup=false',
            'DemoData__ResetOnStartup=false', 'HttpsRedirection__Enabled=false', 'Proxy__ForwardLimit=2', 'RateLimit__LoginPorMinuto=30') | Out-Null
    }

    # Nothing secret stays in memory longer than needed
    $adminPassword = $null; $connection = $null; $jwtKey = $null; $pinSecret = $null
    $sConnection = $null; $sJwt = $null; $sPin = $null; $sAdmin = $null; $sqlPassword = $null
    $script:Secrets.Clear()
    [GC]::Collect()
}

if ($clientExists) { Write-Info "$ClientApp already exists." }
else {
    Invoke-WithRetry {
        if (Test-AzExists @('containerapp', 'show', '--name', $ClientApp, '--resource-group', $ResourceGroup)) { return }
        Invoke-Az -CliArgs @('containerapp', 'create', '--name', $ClientApp, '--resource-group', $ResourceGroup, '--environment', $EnvironmentName,
            '--image', $PlaceholderImage, '--ingress', 'external', '--target-port', $PlaceholderPort,
            '--min-replicas', '1', '--max-replicas', '1', '--cpu', '0.25', '--memory', '0.5Gi',
            '--user-assigned', $identityResourceId, '--registry-server', $AcrServer, '--registry-identity', $identityResourceId,
            '--env-vars', "API_UPSTREAM=https://$ApiApp.internal.$defaultDomain") | Out-Null
    }
}
$apiAppId = Get-AzValue @('containerapp', 'show', '--name', $ApiApp, '--resource-group', $ResourceGroup, '--query', 'id', '--output', 'tsv')
$clientAppId = Get-AzValue @('containerapp', 'show', '--name', $ClientApp, '--resource-group', $ResourceGroup, '--query', 'id', '--output', 'tsv')
if (-not $apiAppId) { $apiAppId = "$GroupScope/providers/Microsoft.App/containerApps/$ApiApp" }
if (-not $clientAppId) { $clientAppId = "$GroupScope/providers/Microsoft.App/containerApps/$ClientApp" }

# ---------------------------------------------------------------------------------------------------------------------------------
# GitHub Actions identity (OIDC, no secret)
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step "GitHub Actions identity $DeployAppName (federated credential, no password)"
$deployAppId = Get-AzValue @('ad', 'app', 'list', '--display-name', $DeployAppName, '--query', '[0].appId', '--output', 'tsv')
if ($deployAppId) { Write-Info 'App registration already exists.' }
else {
    $deployAppId = (Invoke-Az -CliArgs @('ad', 'app', 'create', '--display-name', $DeployAppName, '--sign-in-audience', 'AzureADMyOrg', '--query', 'appId', '--output', 'tsv')).Output
    if (-not $deployAppId) { $deployAppId = '<deploy-app-client-id>' }
}
$deployPrincipalId = Get-AzValue @('ad', 'sp', 'show', '--id', $deployAppId, '--query', 'id', '--output', 'tsv')
if ($deployPrincipalId) { Write-Info 'Service principal already exists.' }
else {
    # Microsoft Entra needs a moment to see a registration created seconds ago
    $deployPrincipalId = Invoke-WithRetry { (Invoke-Az -CliArgs @('ad', 'sp', 'create', '--id', $deployAppId, '--query', 'id', '--output', 'tsv')).Output }
    if (-not $deployPrincipalId) { $deployPrincipalId = '<deploy-principal-id>' }
}

$credentialBody = @{
    name        = $FederatedCredentialName
    issuer      = 'https://token.actions.githubusercontent.com'
    subject     = $federatedSubject
    description = "Deploy production from main of $ownerName/$repoName (immutable-ID subject)"
    audiences   = @('api://AzureADTokenExchange')
}
$existingSubject = Get-FederatedCredentialSubject $deployAppId $FederatedCredentialName
$credentialExists = ($existingSubject -ne '')
if ($credentialExists -and $existingSubject -eq $federatedSubject) { Write-Info 'Federated credential already exists with the right subject.' }
else {
    $credentialFile = Write-TempJson $credentialBody
    try {
        if ($credentialExists) {
            Invoke-WithRetry { Invoke-Az -CliArgs @('ad', 'app', 'federated-credential', 'update', '--id', $deployAppId, '--federated-credential-id', $FederatedCredentialName, '--parameters', "@$credentialFile") | Out-Null }
        }
        else {
            Invoke-WithRetry { Invoke-Az -CliArgs @('ad', 'app', 'federated-credential', 'create', '--id', $deployAppId, '--parameters', "@$credentialFile") | Out-Null }
        }
    }
    finally { Remove-Item -LiteralPath $credentialFile -Force -ErrorAction SilentlyContinue }
}

Write-Step "Roles of the GitHub identity ($DeployRoleScope)"
Confirm-Role -PrincipalId $deployPrincipalId -Role 'Reader' -Scope $GroupScope
Confirm-Role -PrincipalId $deployPrincipalId -Role 'AcrPush' -Scope $acrId
if ($DeployRoleScope -eq 'Apps') {
    Confirm-Role -PrincipalId $deployPrincipalId -Role 'Contributor' -Scope $apiAppId
    Confirm-Role -PrincipalId $deployPrincipalId -Role 'Contributor' -Scope $clientAppId
    Remove-RoleIfPresent -PrincipalId $deployPrincipalId -Role 'Contributor' -Scope $GroupScope
}
else {
    Confirm-Role -PrincipalId $deployPrincipalId -Role 'Contributor' -Scope $GroupScope
    Remove-RoleIfPresent -PrincipalId $deployPrincipalId -Role 'Contributor' -Scope $apiAppId
    Remove-RoleIfPresent -PrincipalId $deployPrincipalId -Role 'Contributor' -Scope $clientAppId
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Locks: the GitHub identity (Contributor) cannot remove them, so it cannot delete the production data
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step 'Delete locks on the SQL server and the database'
if (Test-AzExists @('lock', 'show', '--name', 'lock-sql-server', '--resource-group', $ResourceGroup, '--resource-name', $SqlServer, '--resource-type', 'Microsoft.Sql/servers')) { Write-Info 'Server lock already exists.' }
else {
    Invoke-Az -CliArgs @('lock', 'create', '--name', 'lock-sql-server', '--lock-type', 'CanNotDelete', '--resource-group', $ResourceGroup, '--resource-name', $SqlServer, '--resource-type', 'Microsoft.Sql/servers') | Out-Null
}
if (Test-AzExists @('lock', 'show', '--name', 'lock-sql-db', '--resource-group', $ResourceGroup, '--resource-name', $DatabaseName, '--resource-type', 'Microsoft.Sql/servers/databases', '--parent', "servers/$SqlServer")) { Write-Info 'Database lock already exists.' }
else {
    Invoke-Az -CliArgs @('lock', 'create', '--name', 'lock-sql-db', '--lock-type', 'CanNotDelete', '--resource-group', $ResourceGroup, '--resource-name', $DatabaseName, '--resource-type', 'Microsoft.Sql/servers/databases', '--parent', "servers/$SqlServer") | Out-Null
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Budget alert
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step "Budget alert of USD $BudgetAmountUsd a month"
$budgetUrl = "https://management.azure.com$GroupScope/providers/Microsoft.Consumption/budgets/$BudgetName`?api-version=2023-05-01"
if ((Invoke-Az -CliArgs @('rest', '--method', 'get', '--url', $budgetUrl, '--output', 'none') -ReadOnly -AllowFail).ExitCode -eq 0) { Write-Info 'Budget already exists.' }
else {
    $notification = { param($threshold, $type) @{ enabled = $true; operator = 'GreaterThan'; threshold = $threshold; thresholdType = $type; contactEmails = @($budgetEmail) } }
    $budgetBody = @{
        properties = @{
            category      = 'Cost'
            amount        = $BudgetAmountUsd
            timeGrain     = 'Monthly'
            timePeriod    = @{ startDate = (Get-Date -Day 1).ToString('yyyy-MM-01') + 'T00:00:00Z'; endDate = '2036-12-31T00:00:00Z' }
            notifications = @{
                actual80    = (& $notification 80 'Actual')
                actual100   = (& $notification 100 'Actual')
                forecast100 = (& $notification 100 'Forecast')
            }
        }
    }
    $budgetFile = Write-TempJson $budgetBody
    try { Invoke-Az -CliArgs @('rest', '--method', 'put', '--url', $budgetUrl, '--body', "@$budgetFile", '--output', 'none') | Out-Null }
    finally { Remove-Item -LiteralPath $budgetFile -Force -ErrorAction SilentlyContinue }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# GitHub: what the deploy workflow reads (identifiers, not secrets of value; stored as repository secrets as agreed)
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step "GitHub repository secrets and variable on $GitHubRepo"
Invoke-Gh -CliArgs @('secret', 'set', 'AZURE_CLIENT_ID', '--repo', $GitHubRepo, '--body', $deployAppId) | Out-Null
Invoke-Gh -CliArgs @('secret', 'set', 'AZURE_TENANT_ID', '--repo', $GitHubRepo, '--body', $tenantId) | Out-Null
Invoke-Gh -CliArgs @('secret', 'set', 'AZURE_SUBSCRIPTION_ID', '--repo', $GitHubRepo, '--body', $subscriptionId) | Out-Null
Invoke-Gh -CliArgs @('variable', 'set', 'ACR_NAME', '--repo', $GitHubRepo, '--body', $AcrName) | Out-Null

# ---------------------------------------------------------------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------------------------------------------------------------
Write-Step 'Done'
$clientFqdn = Get-AzValue @('containerapp', 'show', '--name', $ClientApp, '--resource-group', $ResourceGroup, '--query', 'properties.configuration.ingress.fqdn', '--output', 'tsv')
if (-not $clientFqdn) { $clientFqdn = '<client-address-after-creation>' }
Write-Info "Production address : https://$clientFqdn"
Write-Info "Resource group     : $ResourceGroup"
Write-Info "Registry           : $AcrServer"
Write-Info "SQL server         : $sqlFqdn (database $DatabaseName)"
Write-Host ''
Write-Host 'Next: check each resource in the Azure portal (docs/production.md, "What to check afterwards"), then run the workflow' -ForegroundColor Green
Write-Host '"Deploy production" from the Actions tab of the repository. Until it runs, the address shows a placeholder page, not the app.' -ForegroundColor Green
