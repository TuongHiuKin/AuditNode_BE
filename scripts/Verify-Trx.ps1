[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

$resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop
[xml] $trx = Get-Content -LiteralPath $resolvedPath -Raw

$counters = $trx.TestRun.ResultSummary.Counters
if ($null -eq $counters) {
    throw "TRX '$resolvedPath' does not contain ResultSummary/Counters."
}

function Read-Counter {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement] $Counters,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $rawValue = $Counters.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($rawValue)) {
        return 0
    }

    $value = 0
    if (-not [int]::TryParse($rawValue, [ref] $value)) {
        throw "TRX counter '$Name' has invalid value '$rawValue'."
    }

    return $value
}

$total = Read-Counter -Counters $counters -Name 'total'
$executed = Read-Counter -Counters $counters -Name 'executed'
$passed = Read-Counter -Counters $counters -Name 'passed'

$violations = [System.Collections.Generic.List[string]]::new()
if ($total -le 0) {
    $violations.Add("total must be greater than zero (actual: $total)")
}
if ($executed -le 0) {
    $violations.Add("executed must be greater than zero (actual: $executed)")
}
if ($executed -ne $total) {
    $violations.Add("executed must equal total (executed: $executed, total: $total)")
}
if ($passed -ne $total) {
    $violations.Add("passed must equal total (passed: $passed, total: $total)")
}

$prohibitedCounters = @(
    'failed',
    'error',
    'timeout',
    'aborted',
    'notExecuted',
    'notRunnable',
    'disconnected',
    'warning',
    'inconclusive',
    'passedButRunAborted',
    'inProgress',
    'pending',
    'skipped'
)

foreach ($counterName in $prohibitedCounters) {
    $counterValue = Read-Counter -Counters $counters -Name $counterName
    if ($counterValue -ne 0) {
        $violations.Add("$counterName must be zero (actual: $counterValue)")
    }
}

$forbiddenOutcomes = @('Failed', 'Error', 'Timeout', 'Aborted', 'NotExecuted', 'NotRunnable', 'Disconnected', 'Warning', 'Inconclusive', 'Skipped')
$resultNodes = @($trx.TestRun.Results.UnitTestResult)
foreach ($result in $resultNodes) {
    if ($null -ne $result -and $forbiddenOutcomes -contains [string] $result.outcome) {
        $violations.Add("test '$($result.testName)' has forbidden outcome '$($result.outcome)'")
    }
}

if ($violations.Count -gt 0) {
    throw "TRX verification failed for '$resolvedPath': $($violations -join '; ')."
}

Write-Host "TRX verification passed: total=$total, executed=$executed, passed=$passed, skipped/not-executed/failed=0."
