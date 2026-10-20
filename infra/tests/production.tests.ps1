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
Write-Host '== ConvertTo-ProtectedList with none, one and many usernames'
$BootstrapUsername = 'admin'
. ([scriptblock]::Create((Get-ScriptFunctionText @('ConvertTo-ProtectedList'))))
function Test-Throws([scriptblock]$Action) { try { & $Action | Out-Null; return $false } catch { return $true } }

Assert-That ((ConvertTo-ProtectedList '') -ceq 'admin') 'none (empty text): only the bootstrap admin'
Assert-That ((ConvertTo-ProtectedList '   ') -ceq 'admin') 'none (blanks): only the bootstrap admin'
Assert-That ((ConvertTo-ProtectedList ' , ,') -ceq 'admin') 'none (only separators): only the bootstrap admin'
Assert-That ((ConvertTo-ProtectedList 'dueno.real') -ceq 'admin,dueno.real') 'one: the bootstrap admin first, then the name'
Assert-That ((ConvertTo-ProtectedList ' dueno.real , otro-user,mas_1 ') -ceq 'admin,dueno.real,otro-user,mas_1') 'many: spaces around the commas are trimmed, the order is kept'
Assert-That ((ConvertTo-ProtectedList 'Admin,dueno,DUENO,admin') -ceq 'admin,dueno') 'repeats (any case, the bootstrap admin too) are written once'
Assert-That (Test-Throws { ConvertTo-ProtectedList 'dueno real' }) 'a space inside a name is refused'
Assert-That (Test-Throws { ConvertTo-ProtectedList 'due;no' }) 'a symbol outside . _ - is refused'
Assert-That (Test-Throws { ConvertTo-ProtectedList 'ab' }) 'a name shorter than 3 characters is refused'
Assert-That (Test-Throws { ConvertTo-ProtectedList ('x' * 51) }) 'a name longer than 50 characters is refused'
Assert-That (Test-Throws { ConvertTo-ProtectedList 'bueno,mal nombre' }) 'one bad name among good ones refuses the whole list'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== No list from az is parsed with ConvertFrom-Json in the script'
$jsonLines = @(Get-Content $scriptPath | Where-Object { $_ -match 'ConvertFrom-Json' -and $_ -notmatch '^\s*#' })
Assert-That ($jsonLines.Count -ge 1) "the script uses ConvertFrom-Json ($($jsonLines.Count) lines): each one must read a single object"
$piped = @($jsonLines | Where-Object { $_ -match 'ConvertFrom-Json[^|]*\|' })
Assert-That ($piped.Count -eq 0) 'nothing is piped on from ConvertFrom-Json (Where-Object, ForEach-Object, Select-Object)'
Assert-That ((Get-Content -Raw $scriptPath) -notmatch "federated-credential',\s*'list'") 'the script does not call federated-credential list'
Assert-That ((Get-Content -Raw $scriptPath) -notmatch "@\('lock'|'lock',\s*'(show|create|list)'") 'the script never calls az lock (locks go by full resource id through az rest)'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== -DryRun against the fake az and gh'
$mutating = '^(az (extension add|provider register|group create|acr create|monitor log-analytics workspace (create|update)|identity create|role assignment (create|delete)|containerapp ((env )?create|update|secret set)|sql (server|db)( str-policy| firewall-rule)? (create|update|set)|ad (app|sp) create|ad app federated-credential (create|update)|lock create|rest --method put)|gh (secret|variable) set)'

function Invoke-Script([string]$State, [string]$Offer = '', [switch]$Real, [string]$Locks = '', [switch]$BudgetDenied, [string]$Protected = '', [string[]]$Stdin = @('SI'), [string[]]$ScriptArgs = @()) {
    $log = Join-Path ([System.IO.Path]::GetTempPath()) ("cdf-fake-" + [guid]::NewGuid().ToString('N') + '.log')
    $oldPath = $env:PATH
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue' # an error of the script on stderr is an answer to read, not a failure of the test
        $env:PATH = "$fakeDir;$oldPath"
        $env:CDF_FAKE_STATE = $State
        $env:CDF_FAKE_LOG = $log
        $env:CDF_FAKE_OFFER = $Offer
        $env:CDF_FAKE_LOCKS = $Locks
        $env:CDF_FAKE_BUDGET = $(if ($BudgetDenied) { 'denied' } else { '' })
        $env:CDF_FAKE_PROTECTED = $Protected
        if ($Real) {
            # Not a dry run: the commands that change things succeed in the fake and are logged; "SI" answers the confirmation and any
            # further line of $Stdin answers the next question of the script (for example the protected usernames)
            $env:CDF_FAKE_ALLOW_MUTATION = '1'
            $output = $Stdin | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath @ScriptArgs 2>&1 | ForEach-Object { "$_" }
        }
        else {
            $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -DryRun @ScriptArgs 2>&1 | ForEach-Object { "$_" }
        }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
        $env:PATH = $oldPath
        Remove-Item Env:CDF_FAKE_STATE, Env:CDF_FAKE_LOG, Env:CDF_FAKE_OFFER, Env:CDF_FAKE_ALLOW_MUTATION, Env:CDF_FAKE_LOCKS, Env:CDF_FAKE_BUDGET, Env:CDF_FAKE_PROTECTED -ErrorAction SilentlyContinue
    }
    $calls = @()
    if (Test-Path $log) { $calls = @(Get-Content $log); Remove-Item $log -Force }
    return @{ Code = $code; Text = ($output -join "`n"); Calls = $calls }
}

function Find-CallIndex($Calls, [string]$Pattern) {
    for ($i = 0; $i -lt $Calls.Count; $i++) { if ($Calls[$i] -match $Pattern) { return $i } }
    return -1
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
Assert-That ($failed.Text -match '\[dry run\] az rest --method put --url https://management\.azure\.com/subscriptions/[0-9a-f-]+/resourceGroups/rg-control-diario-prod/providers/Microsoft\.Sql/servers/sql-cdf-prod-[0-9a-f]{6}/providers/Microsoft\.Authorization/locks/lock-sql-server\?api-version=2016-09-01') 'state of the failed run: it would lock the SQL server by its full resource id'
Assert-That ($failed.Text -match '\[dry run\] az rest --method put --url \S+/servers/sql-cdf-prod-[0-9a-f]{6}/databases/ControlDiario/providers/Microsoft\.Authorization/locks/lock-sql-db\?api-version=2016-09-01') 'state of the failed run: it would lock the database by its full resource id'
Assert-That (@($failed.Calls | Where-Object { $_ -like 'az lock *' }).Count -eq 0) 'state of the failed run: az lock is never called'
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
Assert-That (@($real.Calls | Where-Object { $_ -like 'az lock *' }).Count -eq 0) 'real run: az lock is never called'
Assert-That (@($real.Calls | Where-Object { $_ -match '^LOCK id=\S+/providers/Microsoft\.Sql/servers/sql-cdf-prod-[0-9a-f]{6}/providers/Microsoft\.Authorization/locks/lock-sql-server level=CanNotDelete api=2016-09-01$' }).Count -eq 1) 'real run: the server lock is created once, CanNotDelete, by resource id'
Assert-That (@($real.Calls | Where-Object { $_ -match '^LOCK id=\S+/providers/Microsoft\.Sql/servers/sql-cdf-prod-[0-9a-f]{6}/databases/ControlDiario/providers/Microsoft\.Authorization/locks/lock-sql-db level=CanNotDelete api=2016-09-01$' }).Count -eq 1) 'real run: the database lock is created once, CanNotDelete, by resource id'
Assert-That (@($real.Calls | Where-Object { $_ -match '^BUDGET amount=25 grain=Monthly start=\d{4}-\d{2}-01T00:00:00Z end=\d{4}-\d{2}-01T00:00:00Z alerts=3 emails=owner@example\.com$' }).Count -eq 1) 'real run: the budget body has USD 25, monthly, a period from the 1st of a month, three alerts to the account e-mail'
$budgetLine = @($real.Calls | Where-Object { $_ -like 'BUDGET*' })[0]
$period = [regex]::Match($budgetLine, 'start=(\d{4})-\d{2}-01T\S* end=(\d{4})-')
Assert-That (([int]$period.Groups[2].Value - [int]$period.Groups[1].Value) -eq 5) 'real run: the budget period is five years (the budgets API limits how long a budget can run)'
Assert-That (@($real.Calls | Where-Object { $_ -like 'MUTATION gh secret set AZURE_*' }).Count -eq 3) 'real run: the three AZURE_* secrets are set'
Assert-That (@($real.Calls | Where-Object { $_ -like 'MUTATION gh variable set ACR_NAME*' }).Count -eq 1) 'real run: ACR_NAME is set'
Assert-That (@($real.Calls | Where-Object { $_ -match 'containerapp create|admin-password|--admin-password' }).Count -eq 0) 'real run: no app is created and no password or secret is sent (cdf-api exists)'
Assert-That ($real.Text -notmatch 'Bootstrap admin password') 'real run: the admin password is not asked for again'
$firstSecret = Find-CallIndex $real.Calls '^MUTATION gh secret set'
$budgetCall = Find-CallIndex $real.Calls '^BUDGET '
Assert-That ($firstSecret -ge 0 -and $budgetCall -ge 0 -and $firstSecret -lt $budgetCall) 'real run: the GitHub secrets and variable are set BEFORE the budget'
Assert-That ($real.Text -match 'Budget\s+: bud-cdf-prod \(USD 25 a month\)') 'real run: the summary says the budget was created'
$leftovers = @(Get-ChildItem ([System.IO.Path]::GetTempPath()) -Filter 'cdf-*.json' -ErrorAction SilentlyContinue)
Assert-That ($leftovers.Count -eq 0) 'real run: the temporary JSON files are deleted'

# The state the owner is in when the first lock call worked and the second failed: the server lock exists, the database one does not
$realHalf = Invoke-Script 'credential' -Real -Locks 'lock-sql-server'
Assert-That ($realHalf.Code -eq 0) "server lock already there, real run: exit code 0 (got $($realHalf.Code))"
Assert-That (@($realHalf.Calls | Where-Object { $_ -like 'LOCK id=*' }).Count -eq 1) 'server lock already there: only one lock is created'
Assert-That (@($realHalf.Calls | Where-Object { $_ -like 'LOCK id=*lock-sql-db*' }).Count -eq 1) 'server lock already there: the one created is the database lock'
Assert-That ($realHalf.Text -match 'Lock lock-sql-server already exists') 'server lock already there: it says so and skips it'

$realBoth = Invoke-Script 'credential' -Real -Locks 'lock-sql-server,lock-sql-db'
Assert-That ($realBoth.Code -eq 0) "both locks already there, real run: exit code 0 (got $($realBoth.Code))"
Assert-That (@($realBoth.Calls | Where-Object { $_ -like 'LOCK id=*' }).Count -eq 0) 'both locks already there: no lock is created again'

# Cost Management not ready (the owner's case right after the upgrade): the budget is refused, everything else must still finish
$refused = Invoke-Script 'credential' -Real -Locks 'lock-sql-server,lock-sql-db' -BudgetDenied
Assert-That ($refused.Code -eq 0) "budget refused: the script still ends with exit code 0 (got $($refused.Code))"
Assert-That (@($refused.Calls | Where-Object { $_ -like 'DENIED budget' }).Count -eq 1) 'budget refused: Azure was asked once and said no'
Assert-That (@($refused.Calls | Where-Object { $_ -like 'MUTATION gh secret set AZURE_*' }).Count -eq 3) 'budget refused: the three AZURE_* secrets are still set'
Assert-That (@($refused.Calls | Where-Object { $_ -like 'MUTATION gh variable set ACR_NAME*' }).Count -eq 1) 'budget refused: ACR_NAME is still set'
Assert-That ((Find-CallIndex $refused.Calls '^MUTATION gh variable set') -lt (Find-CallIndex $refused.Calls '^DENIED budget')) 'budget refused: the GitHub step had already run when the budget was refused'
Assert-That ($refused.Text -match 'the budget alert was NOT created') 'budget refused: a warning says the budget was not created'
Assert-That ($refused.Text -match 'RBACAccessDenied') 'budget refused: it shows what Azure answered'
Assert-That ($refused.Text -match 'Cost Management can take up to 48 hours') 'budget refused: it explains that Cost Management can take up to 48 hours after an upgrade'
Assert-That ($refused.Text -match 'run this script again later') 'budget refused: it says that running the script again later creates the budget'
Assert-That (($refused.Text -match 'Resource groups > rg-control-diario-prod > Cost Management > Budgets') -and ($refused.Text -match 'Amount: 25 \(USD\)') -and ($refused.Text -match 'Monthly')) 'budget refused: the portal steps name the scope, the amount and the period'
Assert-That (($refused.Text -match 'Actual, 80 %') -and ($refused.Text -match 'Actual, 100 %') -and ($refused.Text -match 'Forecasted, 100 %') -and ($refused.Text -match 'Alert recipients \(e-mail\): owner@example\.com')) 'budget refused: the portal steps list the three alerts and the e-mail'
Assert-That ($refused.Text -match '== Done') 'budget refused: it goes on to Done'
Assert-That ($refused.Text -match 'Budget\s+: NOT created') 'budget refused: the summary says the budget was not created'
Assert-That (@(Get-ChildItem ([System.IO.Path]::GetTempPath()) -Filter 'cdf-*.json' -ErrorAction SilentlyContinue).Count -eq 0) 'budget refused: the temporary JSON file is deleted'

$realStale = Invoke-Script 'stalecredential' -Real
Assert-That ($realStale.Code -eq 0) "stale credential, real run: exit code 0 (got $($realStale.Code))"
Assert-That (@($realStale.Calls | Where-Object { $_ -like 'MUTATION az ad app federated-credential update*' }).Count -eq 1) 'stale credential, real run: the credential is updated, not created'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host '== The protected accounts: the secret protected-usernames and the variable AdminUser__Protegidos'

# Fresh environment: the app is created with the secret and the variable in the same call (the dry run shows the call)
$freshProtected = Invoke-Script 'fresh' -ScriptArgs @('-Protegidos', 'dueno.real,otro-user')
Assert-SafeDryRun $freshProtected 'fresh state with -Protegidos'
Assert-That ($freshProtected.Text -match '\[dry run\] az containerapp create --name cdf-api .*protected-usernames=admin,dueno\.real,otro-user') 'fresh state: cdf-api is created with the secret protected-usernames (the bootstrap admin first)'
Assert-That ($freshProtected.Text -match '\[dry run\] az containerapp create --name cdf-api .*AdminUser__Protegidos=secretref:protected-usernames') 'fresh state: cdf-api is created with AdminUser__Protegidos reading that secret'
Assert-That ($freshProtected.Text -match 'cdf-api was just created with the secret protected-usernames') 'fresh state: the protected step says the app already has both'
Assert-That (@($freshProtected.Calls | Where-Object { $_ -like 'az containerapp secret list*' }).Count -eq 0) 'fresh state: there is no app to list the secrets of, and it is not asked'
Assert-That ($freshProtected.Text -notmatch '\[dry run\] az containerapp (secret set|update)') 'fresh state: no second call to add them'
$freshDefault = Invoke-Script 'fresh'
Assert-That ($freshDefault.Text -match '\[dry run\] az containerapp create --name cdf-api .*protected-usernames=admin(\s|$)') 'fresh state, no names given (dry run): only the bootstrap admin is protected'

# Existing app without the secret (the state of the owner when this was written): the script adds both on a rerun
$missing = Invoke-Script 'failedrun' -ScriptArgs @('-Protegidos', 'dueno.real')
Assert-SafeDryRun $missing 'existing app without the secret'
Assert-That (@($missing.Calls | Where-Object { $_ -match '^az containerapp secret list --name cdf-api --resource-group rg-control-diario-prod --query \[\]\.name --output tsv' }).Count -eq 1) 'existing app without the secret: it asks for the names of the secrets (read-only)'
Assert-That ($missing.Text -match '\[dry run\] az containerapp secret set --name cdf-api --resource-group rg-control-diario-prod --secrets protected-usernames=admin,dueno\.real') 'existing app without the secret: it would write the secret'
Assert-That ($missing.Text -match '\[dry run\] az containerapp update --name cdf-api --resource-group rg-control-diario-prod --set-env-vars AdminUser__Protegidos=secretref:protected-usernames') 'existing app without the secret: it would add the variable that reads it'
Assert-That ((Find-CallIndex @($missing.Text -split "`n") 'containerapp secret set') -lt (Find-CallIndex @($missing.Text -split "`n") 'containerapp update')) 'existing app without the secret: the secret is written BEFORE the variable that refers to it'
Assert-That ($missing.Text -notmatch 'containerapp create') 'existing app without the secret: no app is created'
Assert-That ($missing.Text -match 'cdf-api already exists: its secrets are left as they are') 'existing app without the secret: the other secrets are left as they are'

# Existing secret kept
$kept = Invoke-Script 'failedrun' -Protected 'both' -ScriptArgs @('-Protegidos', 'otro.nombre')
Assert-SafeDryRun $kept 'existing secret and variable'
Assert-That ($kept.Text -match 'The secret protected-usernames already exists: it is left as it is') 'existing secret: it says it is kept'
Assert-That ($kept.Text -notmatch '\[dry run\] az containerapp (secret set|update)') 'existing secret and variable: nothing is written, whatever -Protegidos says'
Assert-That ($kept.Text -match 'cdf-api already reads AdminUser__Protegidos') 'existing secret and variable: the variable is already there'

# Secret there, variable missing (a run that stopped in between): only the variable is added
$onlySecret = Invoke-Script 'failedrun' -Protected 'secret'
Assert-SafeDryRun $onlySecret 'secret without the variable'
Assert-That ($onlySecret.Text -notmatch '\[dry run\] az containerapp secret set') 'secret without the variable: the secret is not written again'
Assert-That ($onlySecret.Text -match '\[dry run\] az containerapp update .*--set-env-vars AdminUser__Protegidos=secretref:protected-usernames') 'secret without the variable: the variable is added'

# -ActualizarProtegidos writes the secret again
$update = Invoke-Script 'failedrun' -Protected 'both' -ScriptArgs @('-ActualizarProtegidos', '-Protegidos', 'nuevo.dueno')
Assert-SafeDryRun $update '-ActualizarProtegidos'
Assert-That ($update.Text -match '\[dry run\] az containerapp secret set .*--secrets protected-usernames=admin,nuevo\.dueno') '-ActualizarProtegidos: the secret is written again with the new names'
Assert-That ($update.Text -notmatch '\[dry run\] az containerapp update') '-ActualizarProtegidos: the variable was there, it is not touched'
Assert-That ($update.Text -match 'reads the new names the next time it starts') '-ActualizarProtegidos: it says when the API reads the new names'

# A name the API would not accept stops the script before anything is changed
$invalid = Invoke-Script 'failedrun' -Real -ScriptArgs @('-Protegidos', 'dueno real')
Assert-That ($invalid.Code -ne 0) 'invalid username: the script stops with an error'
Assert-That ($invalid.Text -match "Not a valid username: 'dueno real'") 'invalid username: the message names it'
Assert-That (@($invalid.Calls | Where-Object { $_ -like 'MUTATION*' }).Count -eq 0) 'invalid username: nothing was changed'

# Real runs (not -DryRun) against the fake: what is really sent
$realMissing = Invoke-Script 'failedrun' -Real -Stdin @('SI', ' dueno.real , otro-user ')
Assert-That ($realMissing.Code -eq 0) "real run, secret missing: exit code 0 (got $($realMissing.Code))"
if ($realMissing.Code -ne 0) { Write-Host ($realMissing.Text -split "`n" | Select-Object -Last 12 | Out-String) }
Assert-That (@($realMissing.Calls | Where-Object { $_ -like 'UNHANDLED*' }).Count -eq 0) 'real run, secret missing: no command the fake did not expect'
Assert-That (@($realMissing.Calls | Where-Object { $_ -ceq 'SECRETSET protected-usernames=admin,dueno.real,otro-user' }).Count -eq 1) 'real run, secret missing: the names typed are written once, with the bootstrap admin first'
Assert-That (@($realMissing.Calls | Where-Object { $_ -ceq 'ENVSET AdminUser__Protegidos=secretref:protected-usernames' }).Count -eq 1) 'real run, secret missing: the variable is added once'
Assert-That ((Find-CallIndex $realMissing.Calls '^SECRETSET ') -lt (Find-CallIndex $realMissing.Calls '^ENVSET ')) 'real run, secret missing: the secret is written before the variable'
Assert-That ($realMissing.Text -notmatch 'Bootstrap admin password') 'real run, secret missing: the admin password is not asked for'

$realEmpty = Invoke-Script 'failedrun' -Real -Stdin @('SI', '')
Assert-That (@($realEmpty.Calls | Where-Object { $_ -ceq 'SECRETSET protected-usernames=admin' }).Count -eq 1) 'real run, nothing typed: only the bootstrap admin is protected'

$realBadThenGood = Invoke-Script 'failedrun' -Real -Stdin @('SI', 'mal nombre', 'dueno.real')
Assert-That ($realBadThenGood.Code -eq 0) 'real run, a bad name and then a good list: the script asks again and goes on'
Assert-That ($realBadThenGood.Text -match "Not a valid username: 'mal nombre'") 'real run, a bad name: it says what is wrong'
Assert-That (@($realBadThenGood.Calls | Where-Object { $_ -ceq 'SECRETSET protected-usernames=admin,dueno.real' }).Count -eq 1) 'real run, a bad name and then a good list: only the good list is written'

$realKept = Invoke-Script 'failedrun' -Real -Protected 'both'
Assert-That ($realKept.Code -eq 0) 'real run, secret and variable exist: exit code 0'
Assert-That (@($realKept.Calls | Where-Object { $_ -like 'SECRETSET*' -or $_ -like 'ENVSET*' }).Count -eq 0) 'real run, secret and variable exist: neither is touched'

$realUpdate = Invoke-Script 'failedrun' -Real -Protected 'both' -Stdin @('SI', 'nuevo.dueno') -ScriptArgs @('-ActualizarProtegidos')
Assert-That ($realUpdate.Code -eq 0) 'real run -ActualizarProtegidos: exit code 0'
Assert-That (@($realUpdate.Calls | Where-Object { $_ -ceq 'SECRETSET protected-usernames=admin,nuevo.dueno' }).Count -eq 1) 'real run -ActualizarProtegidos: the new names are written'
Assert-That (@($realUpdate.Calls | Where-Object { $_ -like 'ENVSET*' }).Count -eq 0) 'real run -ActualizarProtegidos: the variable is not touched'

# The names the script and the deploy agree on
$yaml = Get-Content -Raw (Join-Path (Split-Path -Parent $here) 'containerapps\cdf-api.yaml')
$scriptText = Get-Content -Raw $scriptPath
Assert-That ($yaml -match '(?m)^\s*-\s*name:\s*AdminUser__Protegidos\s*\r?\n\s*secretRef:\s*protected-usernames\s*$') 'cdf-api.yaml reads AdminUser__Protegidos from the secret protected-usernames'
Assert-That (($scriptText -match "\`$ProtectedSecretName = 'protected-usernames'") -and ($scriptText -match "\`$ProtectedEnvName = 'AdminUser__Protegidos'")) 'production.ps1 uses the same secret and variable names as cdf-api.yaml'
Assert-That ($scriptText -match "\`$BootstrapUsername = 'admin'") 'production.ps1 protects the bootstrap username of the API (admin)'

# ---------------------------------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "$script:Passed passed, $script:Failures failed"
if ($script:Failures -gt 0) { exit 1 }
exit 0
