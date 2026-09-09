<#
.SYNOPSIS
    Creates (or updates) the "OrderSphere" Seq dashboard described in docs/logging.md.

.DESCRIPTION
    Builds every chart from a plain-text Seq query via the (undocumented but stable)
    `api/dashboards/query/template` endpoint, which parses "select ... from stream where ...
    group by ... order by ... limit ..." into the structured ChartQuery JSON the dashboard
    entity needs - this avoids hand-writing that JSON and keeps every query readable and
    independently testable with `seqcli query -q "..."`.

    Idempotent: if a dashboard titled "OrderSphere" already exists it is replaced in place
    (same id, next version) instead of creating a duplicate.

.PARAMETER ServerUrl
    Base URL of the Seq server, e.g. http://localhost:51238. Find it from the Aspire dashboard's
    resource list (the "seq" resource's http endpoint) - Aspire assigns the port dynamically per
    run. Defaults to the seqcli connection profile's serverUrl if not given.

.EXAMPLE
    ./create-ordersphere-dashboard.ps1 -ServerUrl http://localhost:51238
#>
param(
    [string]$ServerUrl
)

$ErrorActionPreference = "Stop"

if (-not $ServerUrl) {
    $seqCliConfigPath = Join-Path $env:USERPROFILE "SeqCli.json"
    if (Test-Path $seqCliConfigPath) {
        $ServerUrl = (Get-Content $seqCliConfigPath -Raw | ConvertFrom-Json).connection.serverUrl
    }
    if (-not $ServerUrl) {
        throw "No -ServerUrl given and no seqcli connection profile found. Pass -ServerUrl explicitly (see the 'seq' resource's http endpoint in the Aspire dashboard)."
    }
}
$server = $ServerUrl.TrimEnd('/')

# Exact filter text of the built-in signals (GET /api/signals/<id>), inlined because setting
# ChartQuery.SignalExpression via POST /api/dashboards/ returns a 500 on Seq 2026.1/17114 -
# observed and worked around, not investigated further upstream.
$ERR_FILTER  = "@Level in ['f', 'fa', 'fat', 'ftl', 'fata', 'fatl', 'fatal', 'c', 'cr', 'cri', 'crt', 'crit', 'critical', 'alert', 'emerg', 'panic', 'e', 'er', 'err', 'eror', 'erro', 'error'] ci"
$WARN_FILTER = "@Level in ['w', 'wa', 'war', 'wrn', 'warn', 'warning'] ci"
$EXC_FILTER  = "@Exception is not null"
$LOGS_FILTER = "not(has(@Start))"

function New-ChartQuery {
    param(
        [string]$Query,
        [string]$Type,
        [string]$Palette = "Default",
        [bool]$FillToZero = $false,
        [bool]$BarOverlaySum = $false
    )
    $uri = "$server/api/dashboards/query/template?q=" + [System.Uri]::EscapeDataString($Query)
    $cq = Invoke-RestMethod -Uri $uri -Method Get

    $cq.DisplayStyle.Type = $Type
    $cq.DisplayStyle.LineFillToZeroY = $FillToZero
    $cq.DisplayStyle.BarOverlaySum = $BarOverlaySum
    $cq.DisplayStyle.Palette = $Palette
    return $cq
}

function New-Chart {
    param(
        [string]$Title,
        [string]$Description,
        [array]$Queries,
        [int]$Width,
        [int]$Height
    )
    return [ordered]@{
        Id = $null
        Title = $Title
        Description = $Description
        SignalExpression = $null
        Queries = $Queries
        DisplayStyle = [ordered]@{ WidthColumns = $Width; HeightRows = $Height }
    }
}

$charts = @()

# --- Row A: KPI strip ----------------------------------------------------
$charts += New-Chart -Title "Total Events" -Description "All log records and spans in the selected window." -Width 2 -Height 1 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream" -Type "Value")
)
$charts += New-Chart -Title "Errors" -Description "Error, Critical or Fatal level records (built-in Errors signal filter)." -Width 2 -Height 1 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where $ERR_FILTER" -Type "Value")
)
$charts += New-Chart -Title "Warnings" -Description "Includes every Result failure - the normal level for `"not found`", `"declined`"." -Width 2 -Height 1 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where $WARN_FILTER" -Type "Value")
)
$charts += New-Chart -Title "Exceptions" -Description "Records carrying a captured exception." -Width 2 -Height 1 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where $EXC_FILTER" -Type "Value")
)
$charts += New-Chart -Title "Active Services" -Description "Distinct service.name values reporting in the window." -Width 2 -Height 1 -Queries @(
    (New-ChartQuery -Query "select count(distinct(@Resource.service.name)) as count from stream" -Type "Value")
)
$charts += New-Chart -Title "Distinct Traces" -Description "Distinct W3C trace ids - one per request/message flow." -Width 2 -Height 1 -Queries @(
    (New-ChartQuery -Query "select count(distinct(@TraceId)) as count from stream" -Type "Value")
)

# --- Row B: trends ---------------------------------------------------------
$charts += New-Chart -Title "Events Over Time by Level" -Description "Log volume over time, split by level." -Width 8 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream group by @Level" -Type "Line" -FillToZero $false)
)
$charts += New-Chart -Title "Errors & Exceptions Over Time" -Description "Same rule as the built-in Overview dashboard: any exception, or an Error/Fatal/Critical level record." -Width 4 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where $EXC_FILTER or $ERR_FILTER" -Type "Bar" -Palette "Reds" -BarOverlaySum $true)
)

# --- Row C: per-service breakdown ------------------------------------------
$charts += New-Chart -Title "Events by Service" -Description "Volume per OrderSphere service (service.name resource attribute)." -Width 6 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream group by @Resource.service.name order by count desc" -Type "Bar")
)
$charts += New-Chart -Title "Warnings by Service" -Description "Where the expected-failure volume (Result failures, declined operations) is concentrated." -Width 6 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where $WARN_FILTER group by @Resource.service.name order by count desc" -Type "Bar")
)

# --- Row D: Service Bus / workers -------------------------------------------
$charts += New-Chart -Title "Messages by Queue" -Description "Service Bus queue activity from MessageProcessingScope-tagged records." -Width 6 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where queue is not null group by queue order by count desc" -Type "Bar")
)
$charts += New-Chart -Title "Integration Events Processed" -Description "Per-event-type volume for records inside an actual message-processing scope (message_id set). Empty until Service Bus traffic flows." -Width 6 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where message_id is not null group by event_type order by count desc" -Type "Bar")
)

# --- Row E: multi-tenancy and correlation -----------------------------------
$charts += New-Chart -Title "Events by Tenant" -Description "Log volume per tenant_id. Empty until an authenticated, tenant-scoped request runs." -Width 6 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where tenant_id is not null group by tenant_id order by count desc limit 20" -Type "Table")
)
$charts += New-Chart -Title "Top Correlation Chains" -Description "Requests/flows producing the most log records - a chatty or looping request stands out here." -Width 6 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where correlation_id is not null group by correlation_id order by count desc limit 20" -Type "Table")
)

# --- Row F: diagnostics ------------------------------------------------------
$charts += New-Chart -Title "Top Message Templates" -Description "Noisiest log statements: spans excluded (Logs-signal rule), Azure Service Bus SDK receive-loop/link-management chatter filtered out." -Width 12 -Height 2 -Queries @(
    (New-ChartQuery -Query "select count(*) as count from stream where $LOGS_FILTER and @MessageTemplate not like '%ReceiveBatchAsync%' and @MessageTemplate not like '%MessagePeekAsync%' and @MessageTemplate not like '%anagement link%' ci group by @MessageTemplate order by count desc limit 15" -Type "Table")
)
$charts += New-Chart -Title "Recent Warnings & Errors" -Description "Live feed - the last 20 Warning/Error/Fatal records across every service." -Width 12 -Height 2 -Queries @(
    (New-ChartQuery -Query "select @Timestamp, @Level, @Resource.service.name, @MessageTemplate from stream where $WARN_FILTER or $ERR_FILTER order by @Timestamp desc limit 20" -Type "Table")
)

# Idempotent: replace the existing "OrderSphere" dashboard in place rather than duplicating it.
# The list endpoint's Links.Self carries the current ?version=N, reused verbatim for the PUT -
# the server bumps the version itself. PUT requires the body's Id to match the target resource,
# hence setting it conditionally below rather than in the object literal (POST must NOT send one).
$dashboardList = Invoke-RestMethod -Uri "$server/api/dashboards?shared=true" -Method Get
$existing = $dashboardList | Where-Object { $_.Title -eq "OrderSphere" } | Select-Object -First 1

$dashboard = [ordered]@{
    Id = if ($existing) { $existing.Id } else { $null }
    OwnerId = $null
    Title = "OrderSphere"
    IsProtected = $false
    SignalExpression = $null
    Charts = $charts
}

$json = $dashboard | ConvertTo-Json -Depth 12

if ($existing) {
    $result = Invoke-RestMethod -Uri "$server/$($existing.Links.Self)" -Method Put -Body $json -ContentType "application/json"
    Write-Output "Updated existing dashboard: $($result.Id)"
} else {
    $result = Invoke-RestMethod -Uri "$server/api/dashboards/" -Method Post -Body $json -ContentType "application/json"
    Write-Output "Created dashboard: $($result.Id)"
}

Write-Output "Open it at: $server/#/dashboards/$($result.Id)"
