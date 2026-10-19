# A stand-in for `az` and `gh` used only by infra/tests/production.tests.ps1. It answers the READ-ONLY commands that
# infra/production.ps1 -DryRun sends, from a pretend state of the Azure subscription chosen by $env:CDF_FAKE_STATE:
#   fresh            nothing exists yet
#   failedrun        what the first real run left: everything up to the container apps, the app registration and its service
#                    principal, but no federated credential, no locks, no budget
#   credential       like failedrun, with the federated credential already there and right
#   stalecredential  like failedrun, with a federated credential whose subject is wrong
# $env:CDF_FAKE_ALLOW_MUTATION=1 lets commands that change things succeed (and logs them), for a run of the script that is not a dry run;
# the files passed as @file (federated credential, budget) are read and their content is logged.
# $env:CDF_FAKE_OFFER overrides the subscription offer (default PayAsYouGo_2014-09-01). Every call is appended to $env:CDF_FAKE_LOG.
# Anything it does not know is logged as UNHANDLED and fails, so a new read-only call in the script cannot go unnoticed.
[CmdletBinding(PositionalBinding = $false)]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$All)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tool = $All[0]
$command = (@($All | Select-Object -Skip 1)) -join ' '
$state = $env:CDF_FAKE_STATE
if (-not $state) { $state = 'fresh' }
$offer = $env:CDF_FAKE_OFFER
if (-not $offer) { $offer = 'PayAsYouGo_2014-09-01' }

if ($env:CDF_FAKE_LOG) { Add-Content -LiteralPath $env:CDF_FAKE_LOG -Value "$tool $command" }

$built = ($state -ne 'fresh') # the group, registry, SQL, environment and apps exist
$subscriptionId = '11111111-1111-1111-1111-111111111111'
$group = "/subscriptions/$subscriptionId/resourceGroups/rg-control-diario-prod"

function Reply([string]$Text, [int]$Code = 0) {
    if ($Text) { [Console]::Out.WriteLine($Text) }
    exit $Code
}
function NotFound { [Console]::Error.WriteLine('ERROR: (ResourceNotFound) The resource was not found.'); exit 3 }
function Exists([bool]$Yes, [string]$Text = '') { if ($Yes) { Reply $Text } else { NotFound } }

if ($tool -eq 'gh') {
    if ($command -match '^auth status') { Reply 'Logged in to github.com' }
    if ($command -match '^api repos/') { Reply '{"id":123,"name":"Control-Diario-Planta","private":true,"owner":{"login":"isaias-muga","id":456}}' }
}
elseif ($tool -eq 'az') {
    switch -Regex ($command) {
        '^account show' { Reply ('{"id":"' + $subscriptionId + '","tenantId":"22222222-2222-2222-2222-222222222222","name":"Fake subscription","user":{"name":"owner@example.com","type":"user"}}') }
        '^rest --method get --url https://management\.azure\.com/subscriptions/[^/ ]+\?api-version' { Reply ('{"subscriptionPolicies":{"quotaId":"' + $offer + '","spendingLimit":"Off"}}') }
        '^rest --method get --url .*budgets' { NotFound }
        '^provider show' { Reply 'Registered' }
        '^group show' { Exists $built 'brazilsouth' }
        '^acr check-name' { Reply 'true' }
        '^acr show .*--query id' { Exists $built "$group/providers/Microsoft.ContainerRegistry/registries/crcontroldiario123456" }
        '^acr show' { Exists $built }
        '^monitor log-analytics workspace show .*--query customerId' { Exists $built '33333333-3333-3333-3333-333333333333' }
        '^monitor log-analytics workspace show' { Exists $built }
        '^monitor log-analytics workspace get-shared-keys' { Reply 'fake-workspace-key' }
        '^identity show .*--query principalId' { Exists $built '44444444-4444-4444-4444-444444444444' }
        '^identity show .*--query id' { Exists $built "$group/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-cdf-pull" }
        '^identity show' { Exists $built }
        '^role assignment list .*--role AcrPull .*--scope \S*registries' { Reply $(if ($built) { '1' } else { '0' }) }
        '^role assignment list' { Reply '0' }
        '^containerapp env show .*--query properties\.defaultDomain' { Exists $built 'fake-domain.brazilsouth.azurecontainerapps.io' }
        '^containerapp env show' { Exists $built }
        '^containerapp show .*--name (cdf-api|cdf-client) .*--query id' { Exists $built "$group/providers/Microsoft.App/containerApps/$($Matches[1])" }
        '^containerapp show .*--query properties\.configuration\.ingress\.fqdn' { Exists $built 'cdf-client.fake-domain.brazilsouth.azurecontainerapps.io' }
        '^containerapp show' { Exists $built }
        '^sql server show .*--query fullyQualifiedDomainName' { Exists $built 'sql-cdf-prod-123456.database.windows.net' }
        '^sql server show' { Exists $built }
        '^sql server firewall-rule show' { Exists $built }
        '^sql db show' { Exists $built }
        '^ad app list' { Reply $(if ($built) { '55555555-5555-5555-5555-555555555555' } else { '' }) }
        '^ad sp show' { Exists $built '66666666-6666-6666-6666-666666666666' }
        '^ad app federated-credential show' {
            if ($state -eq 'credential') { Reply 'repo:isaias-muga@456/Control-Diario-Planta@123:ref:refs/heads/main' }
            if ($state -eq 'stalecredential') { Reply 'repo:isaias-muga/Control-Diario-Planta:ref:refs/heads/main' }
            NotFound
        }
        '^ad app federated-credential list' { Reply '[]' } # what the first run parsed; the script must not ask for it any more
        '^lock show' { NotFound }
    }
}

$changes = '^(az (extension add|provider register|group create|acr create|monitor log-analytics workspace (create|update)|identity create|role assignment (create|delete)|containerapp (env )?create|sql (server|db)( str-policy| firewall-rule)? (create|update|set)|ad (app|sp) create|ad app federated-credential (create|update)|lock create|rest --method put)|gh (secret|variable) set)'
if ($env:CDF_FAKE_ALLOW_MUTATION -eq '1' -and "$tool $command" -match $changes) {
    if ($command -match '--(parameters|body) @(\S+)') {
        $file = $Matches[2]
        $json = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
        if ($command -match 'federated-credential') {
            Add-Content -LiteralPath $env:CDF_FAKE_LOG -Value "CREDENTIAL name=$($json.name) issuer=$($json.issuer) subject=$($json.subject) audience=$(@($json.audiences) -join ',')"
        }
        else {
            $emails = @($json.properties.notifications.PSObject.Properties | ForEach-Object { $_.Value.contactEmails } | Select-Object -Unique) -join ','
            Add-Content -LiteralPath $env:CDF_FAKE_LOG -Value "BUDGET amount=$($json.properties.amount) grain=$($json.properties.timeGrain) alerts=$(@($json.properties.notifications.PSObject.Properties).Count) emails=$emails"
        }
    }
    Add-Content -LiteralPath $env:CDF_FAKE_LOG -Value "MUTATION $tool $command"
    Reply ''
}

if ($env:CDF_FAKE_LOG) { Add-Content -LiteralPath $env:CDF_FAKE_LOG -Value "UNHANDLED $tool $command" }
[Console]::Error.WriteLine("fake-cli: unhandled command: $tool $command")
exit 1
