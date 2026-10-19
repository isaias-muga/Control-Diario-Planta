<#
.SYNOPSIS
  Tests for infra/production.ps1. Run them with Windows PowerShell 5.1 (the shell the script is written for):

      powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\infra\tests\production.tests.ps1

  They need no Azure account: a stand-in for az and gh (infra/tests/fake-cli) answers the read-only commands of a -DryRun.
  Exit code 0 = everything passed.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$scriptPath = Join-Path (Split-Path -Parent $here) 'production.ps1'
$fakeDir = Join-Path $here 'fake-cli'
$script:Failures = 0
$script:Passed = 0

function Assert-That([bool]$Condition, [string]$Message) {
    if ($Condition) { $script:Passed++; Write-Host "  ok    $Message" }
    else { $script:Failures++; Write-Host "  FAIL  $Message" -ForegroundColor Red }
}

Write-Host "PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
if ($PSVersionTable.PSVersion.Major -ne 5) { Write-Host 'NOTE: these tests prove the fix only when run with Windows PowerShell 5.1 (powershell.exe).' -ForegroundColor Yellow }

# The source of some functions of the script (without running the script), to define them here and test them on their own
function Get-ScriptFunctionText([string[]]$Names) {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$null, [ref]$null)
    $found = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false) | Where-Object { $Names -contains $_.Name }
    return (@($found | ForEach-Object { $_.Extent.Text }) -join "`n")
}

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== The script parses'
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$null, [ref]$parseErrors)
Assert-That ($parseErrors.Count -eq 0) 'production.ps1 has no syntax errors'
Assert-That (-not ((Get-Content -Raw $scriptPath) -match '[^\x00-\x7f]')) 'production.ps1 is plain ASCII (Windows PowerShell 5.1 reads a file without a BOM as ANSI)'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== The cause: ConvertFrom-Json of a list in Windows PowerShell 5.1'
function Find-CredentialTheOldWay([string]$Json) {
    # what line 463 did
    return @($Json | ConvertFrom-Json) | Where-Object { $_.name -eq 'github-main' } | Select-Object -First 1
}
if ($PSVersionTable.PSVersion.Major -eq 5) {
    $threw = $false
    try { [void](Find-CredentialTheOldWay '[]') } catch { $threw = $true }
    Assert-That $threw 'the old pattern throws on "[]" under strict mode (the failure of the first run)'
    $wrong = Find-CredentialTheOldWay '[{"name":"a","subject":"s1"},{"name":"github-main","subject":"s2"}]'
    Assert-That (([string]$wrong.subject) -ne 's2') 'the old pattern also picks the wrong credential when there are several'
}
else { Write-Host '  skip  (the old pattern only fails on Windows PowerShell 5.1)' }

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== Get-FederatedCredentialSubject with an empty answer, one credential and several lines'
. ([scriptblock]::Create((Get-ScriptFunctionText @('Get-FederatedCredentialSubject'))))
$script:StubOutput = ''
function Get-AzValue([object[]]$CliArgs) { return $script:StubOutput }

$script:StubOutput = ''
Assert-That ((Get-FederatedCredentialSubject 'app' 'github-main') -ceq '') 'no credential (az says nothing) gives an empty subject, no error'
$script:StubOutput = 'repo:owner@1/repo@2:ref:refs/heads/main'
Assert-That ((Get-FederatedCredentialSubject 'app' 'github-main') -ceq 'repo:owner@1/repo@2:ref:refs/heads/main') 'one credential gives its subject'
$script:StubOutput = "first-subject`r`nsecond-subject`r`n"
Assert-That ((Get-FederatedCredentialSubject 'app' 'github-main') -ceq 'first-subject') 'several lines give the first one'
$script:StubOutput = "only-subject`n"
Assert-That ((Get-FederatedCredentialSubject 'app' 'github-main') -ceq 'only-subject') 'a trailing newline is ignored'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== No list from az is parsed with ConvertFrom-Json in the script'
$jsonLines = @(Get-Content $scriptPath | Where-Object { $_ -match 'ConvertFrom-Json' -and $_ -notmatch '^\s*#' })
Assert-That ($jsonLines.Count -ge 1) "the script uses ConvertFrom-Json ($($jsonLines.Count) lines): each one must read a single object"
$piped = @($jsonLines | Where-Object { $_ -match 'ConvertFrom-Json[^|]*\|' })
Assert-That ($piped.Count -eq 0) 'nothing is piped on from ConvertFrom-Json (Where-Object, ForEach-Object, Select-Object)'
Assert-That ((Get-Content -Raw $scriptPath) -notmatch "federated-credential',\s*'list'") 'the script does not call federated-credential list'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== -DryRun against the fake az and gh'
$mutating = '^(az (extension add|provider register|group create|acr create|monitor log-analytics workspace (create|update)|identity create|role assignment (create|delete)|containerapp (env )?create|sql (server|db)( str-policy| firewall-rule)? (create|update|set)|ad (app|sp) create|ad app federated-credential (create|update)|lock create|rest --method put)|gh (secret|variable) set)'

function Invoke-Script([string]$State, [string]$Offer = '', [switch]$Real) {
    $log = Join-Path ([System.IO.Path]::GetTempPath()) ("cdf-fake-" + [guid]::NewGuid().ToString('N') + '.log')
    $oldPath = $env:PATH
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue' # an error of the script on stderr is an answer to read, not a failure of the test
        $env:PATH = "$fakeDir;$oldPath"
        $env:CDF_FAKE_STATE = $State
        $env:CDF_FAKE_LOG = $log
        $env:CDF_FAKE_OFFER = $Offer
        if ($Real) {
            # Not a dry run: the commands that change things succeed in the fake and are logged; "SI" answers the confirmation
            $env:CDF_FAKE_ALLOW_MUTATION = '1'
            $output = 'SI' | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath 2>&1 | ForEach-Object { "$_" }
        }
        else {
            $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -DryRun 2>&1 | ForEach-Object { "$_" }
        }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
        $env:PATH = $oldPath
        Remove-Item Env:CDF_FAKE_STATE, Env:CDF_FAKE_LOG, Env:CDF_FAKE_OFFER, Env:CDF_FAKE_ALLOW_MUTATION -ErrorAction SilentlyContinue
    }
    $calls = @()
    if (Test-Path $log) { $calls = @(Get-Content $log); Remove-Item $log -Force }
    return @{ Code = $code; Text = ($output -join "`n"); Calls = $calls }
}

function Assert-SafeDryRun($Run, [string]$Name) {
    Assert-That ($Run.Code -eq 0) "$Name : exit code 0 (got $($Run.Code))"
    if ($Run.Code -ne 0) { Write-Host ($Run.Text -split "`n" | Select-Object -Last 12 | Out-String) }
    Assert-That (@($Run.Calls | Where-Object { $_ -like 'UNHANDLED*' }).Count -eq 0) "$Name : no command the fake did not expect"
    Assert-That (@($Run.Calls | Where-Object { $_ -match $mutating }).Count -eq 0) "$Name : no command that creates or changes anything was sent to az/gh"
    Assert-That (@($Run.Calls | Where-Object { $_ -match 'federated-credential list' }).Count -eq 0) "$Name : federated-credential list is never called"
}

$fresh = Invoke-Script 'fresh'
Assert-SafeDryRun $fresh 'fresh state'
Assert-That ($fresh.Text -match '\[dry run\] az containerapp create --name cdf-api') 'fresh state: it would create cdf-api'
Assert-That ($fresh.Text -match '\[dry run\] az ad app federated-credential create') 'fresh state: it would create the federated credential'
Assert-That ($fresh.Text -notmatch 'admin-password=[^*]') 'fresh state: no secret value in the output'

$failed = Invoke-Script 'failedrun'
Assert-SafeDryRun $failed 'state of the failed run'
Assert-That ($failed.Text -match 'cdf-api already exists: its secrets are left as they are') 'state of the failed run: cdf-api exists, so the admin password is not asked for again'
Assert-That ($failed.Text -notmatch 'containerapp create') 'state of the failed run: no app is created again'
Assert-That ($failed.Text -match '\[dry run\] az ad app federated-credential create') 'state of the failed run: it gets past the step that failed and would create the credential'
Assert-That ($failed.Text -match 'Service principal already exists') 'state of the failed run: the service principal is reused'
Assert-That ($failed.Text -match '\[dry run\] az lock create --name lock-sql-server') 'state of the failed run: it goes on to the locks'
Assert-That ($failed.Text -match '\[dry run\] az rest --method put') 'state of the failed run: it goes on to the budget'
Assert-That ($failed.Text -match '\[dry run\] gh secret set AZURE_CLIENT_ID') 'state of the failed run: it reaches the GitHub secrets'

$right = Invoke-Script 'credential'
Assert-SafeDryRun $right 'credential already right'
Assert-That ($right.Text -match 'Federated credential already exists with the right subject') 'credential already right: left alone'
Assert-That ($right.Text -notmatch 'federated-credential (create|update)') 'credential already right: not created or updated'

$stale = Invoke-Script 'stalecredential'
Assert-SafeDryRun $stale 'stale credential'
Assert-That ($stale.Text -match '\[dry run\] az ad app federated-credential update') 'stale credential: it would be updated'

$trial = Invoke-Script 'fresh' 'FreeTrial_2014-09-01'
Assert-That ($trial.Code -ne 0) 'free trial: the script stops with an error'
Assert-That ($trial.Text -match 'free trial') 'free trial: the message says why'
Assert-That (@($trial.Calls | Where-Object { $_ -match $mutating }).Count -eq 0) 'free trial: nothing was changed'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== The real run (not -DryRun) from the state the failed run left, against the fake'
$real = Invoke-Script 'failedrun' -Real
Assert-That ($real.Code -eq 0) "real run: exit code 0 (got $($real.Code))"
if ($real.Code -ne 0) { Write-Host ($real.Text -split "`n" | Select-Object -Last 12 | Out-String) }
Assert-That (@($real.Calls | Where-Object { $_ -like 'UNHANDLED*' }).Count -eq 0) 'real run: no command the fake did not expect'
Assert-That (@($real.Calls | Where-Object { $_ -match 'federated-credential create' -and $_ -like 'MUTATION*' }).Count -eq 1) 'real run: the federated credential is created once'
Assert-That (@($real.Calls | Where-Object { $_ -like 'CREDENTIAL*' }) -contains 'CREDENTIAL name=github-main issuer=https://token.actions.githubusercontent.com subject=repo:isaias-muga@456/Control-Diario-Planta@123:ref:refs/heads/main audience=api://AzureADTokenExchange') 'real run: the credential file has the immutable-ID subject, the GitHub issuer and the Azure audience'
Assert-That (@($real.Calls | Where-Object { $_ -like 'MUTATION az role assignment create*' }).Count -eq 4) 'real run: four role assignments (Reader, AcrPush, Contributor on each app)'
Assert-That (@($real.Calls | Where-Object { $_ -like 'MUTATION az lock create*' }).Count -eq 2) 'real run: the two locks'
Assert-That (@($real.Calls | Where-Object { $_ -like 'BUDGET amount=25 grain=Monthly alerts=3 emails=owner@example.com' }).Count -eq 1) 'real run: the budget body has USD 25, monthly, three alerts to the account e-mail'
Assert-That (@($real.Calls | Where-Object { $_ -like 'MUTATION gh secret set AZURE_*' }).Count -eq 3) 'real run: the three AZURE_* secrets are set'
Assert-That (@($real.Calls | Where-Object { $_ -like 'MUTATION gh variable set ACR_NAME*' }).Count -eq 1) 'real run: ACR_NAME is set'
Assert-That (@($real.Calls | Where-Object { $_ -match 'containerapp create|admin-password|--admin-password' }).Count -eq 0) 'real run: no app is created and no password or secret is sent (cdf-api exists)'
Assert-That ($real.Text -notmatch 'Bootstrap admin password') 'real run: the admin password is not asked for again'
$leftovers = @(Get-ChildItem ([System.IO.Path]::GetTempPath()) -Filter 'cdf-*.json' -ErrorAction SilentlyContinue)
Assert-That ($leftovers.Count -eq 0) 'real run: the temporary JSON files are deleted'

$realStale = Invoke-Script 'stalecredential' -Real
Assert-That ($realStale.Code -eq 0) "stale credential, real run: exit code 0 (got $($realStale.Code))"
Assert-That (@($realStale.Calls | Where-Object { $_ -like 'MUTATION az ad app federated-credential update*' }).Count -eq 1) 'stale credential, real run: the credential is updated, not created'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "$script:Passed passed, $script:Failures failed"
if ($script:Failures -gt 0) { exit 1 }
exit 0
