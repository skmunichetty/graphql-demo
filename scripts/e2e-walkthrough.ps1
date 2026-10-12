<#
.SYNOPSIS
  End-to-end walkthrough against the running local stack (LOCAL DEVELOPMENT ONLY):
  authenticate -> create estimate -> add line items -> approve -> export -> query export status.

.DESCRIPTION
  Requires Keycloak and PostgreSQL (docker compose) and both APIs (dotnet run or docker compose).
  Each step prints what it sends and what came back, and the script stops at the first unexpected result.
#>
param(
    [string]$EstimatesUrl = 'http://localhost:5101/graphql',
    [string]$AccountingUrl = 'http://localhost:5102/graphql'
)
$ErrorActionPreference = 'Stop'

function Invoke-GraphQL([string]$Url, [string]$Token, [string]$Query, [hashtable]$Variables) {
    $body = @{ query = $Query; variables = $Variables } | ConvertTo-Json -Depth 10
    $headers = @{ 'X-Correlation-ID' = "e2e-$([Guid]::NewGuid().ToString('N').Substring(0, 8))" }
    if ($Token) {
        $headers['Authorization'] = "Bearer $Token"
    }
    return Invoke-RestMethod -Method Post -Uri $Url -Headers $headers -ContentType 'application/json' -Body $body
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "FAILED: $Message"
    }
    Write-Host "  OK  $Message" -ForegroundColor Green
}

Write-Host "`n1. Authenticate (Keycloak, demo password grant)" -ForegroundColor Cyan
$alice = & "$PSScriptRoot/get-token.ps1" -User alice
$bob = & "$PSScriptRoot/get-token.ps1" -User bob
Assert-True ($alice.Length -gt 100) 'alice (tenant-acme) has an access token'

try {
    Invoke-GraphQL $EstimatesUrl $null '{ estimates { nodes { id } } }' @{} | Out-Null
    throw 'FAILED: unauthenticated request was accepted'
}
catch [System.Net.WebException] {
    Assert-True ($_.Exception.Response.StatusCode.value__ -eq 401) 'request without a token is rejected with HTTP 401'
}

Write-Host "`n2. Create a draft estimate" -ForegroundColor Cyan
$create = Invoke-GraphQL $EstimatesUrl $alice @'
mutation Create($input: CreateEstimateInput!) {
  createEstimate(input: $input) {
    estimate { id status version currencyCode }
    errors { ... on UserError { code message } }
  }
}
'@ @{ input = @{ customerName = 'Harbour View Apartments'; customerEmail = 'pm@harbourview.example'; currencyCode = 'AUD' } }
$estimate = $create.data.createEstimate.estimate
Assert-True ($estimate.status -eq 'DRAFT') "created estimate $($estimate.id) as DRAFT, version $($estimate.version)"

Write-Host "`n3. Add line items" -ForegroundColor Cyan
$addLine = @'
mutation Add($input: AddLineItemInput!) {
  addLineItem(input: $input) {
    estimate { id version subtotal taxTotal total lineItems { position description subtotal taxAmount total } }
    errors { ... on UserError { code message } ... on ValidationError { field } }
  }
}
'@
$result = Invoke-GraphQL $EstimatesUrl $alice $addLine @{ input = @{ estimateId = $estimate.id; expectedVersion = $estimate.version; description = 'Structural steel beams'; quantity = 12; unitPrice = 845.50; taxRatePercent = 10 } }
$estimate = $result.data.addLineItem.estimate
$result = Invoke-GraphQL $EstimatesUrl $alice $addLine @{ input = @{ estimateId = $estimate.id; expectedVersion = $estimate.version; description = 'Crane hire (half day)'; quantity = 1; unitPrice = 1250; taxRatePercent = 10 } }
$estimate = $result.data.addLineItem.estimate
Assert-True ($estimate.lineItems.Count -eq 2) 'estimate has 2 line items'
Assert-True ([decimal]$estimate.total -eq 12535.60) "total is 12535.60 AUD (subtotal $($estimate.subtotal) + tax $($estimate.taxTotal))"

$invalid = Invoke-GraphQL $EstimatesUrl $alice $addLine @{ input = @{ estimateId = $estimate.id; expectedVersion = $estimate.version; description = 'Bad'; quantity = 0; unitPrice = 1 } }
Assert-True ($invalid.data.addLineItem.errors[0].code -eq 'VALIDATION_FAILED') 'quantity 0 is rejected with VALIDATION_FAILED (field: quantity)'

$stale = Invoke-GraphQL $EstimatesUrl $alice $addLine @{ input = @{ estimateId = $estimate.id; expectedVersion = 1; description = 'Stale'; quantity = 1; unitPrice = 1 } }
Assert-True ($stale.data.addLineItem.errors[0].code -eq 'CONCURRENCY_CONFLICT') 'stale expectedVersion is rejected with CONCURRENCY_CONFLICT'

$bobView = Invoke-GraphQL $EstimatesUrl $bob 'query($id: UUID!) { estimate(id: $id) { id } }' @{ id = $estimate.id }
Assert-True ($null -eq $bobView.data.estimate) "bob (tenant-bayside) cannot see alice's estimate"

Write-Host "`n4. Try to export the DRAFT estimate (must be rejected)" -ForegroundColor Cyan
$export = @'
mutation Export($input: ExportEstimateInput!) {
  exportEstimate(input: $input) {
    export { id status externalReference exportedTotal currencyCode attemptCount lastErrorCode }
    errors { __typename ... on UserError { code message } }
  }
}
'@
$draftExport = Invoke-GraphQL $AccountingUrl $alice $export @{ input = @{ estimateId = $estimate.id } }
Assert-True ($draftExport.data.exportEstimate.errors[0].code -eq 'ESTIMATE_NOT_APPROVED') 'export of a draft is rejected with ESTIMATE_NOT_APPROVED'

Write-Host "`n5. Approve the estimate" -ForegroundColor Cyan
$approve = Invoke-GraphQL $EstimatesUrl $alice @'
mutation Approve($input: ApproveEstimateInput!) {
  approveEstimate(input: $input) {
    estimate { id status version approvedAt }
    errors { ... on UserError { code message } }
  }
}
'@ @{ input = @{ estimateId = $estimate.id; expectedVersion = $estimate.version } }
$approved = $approve.data.approveEstimate.estimate
Assert-True ($approved.status -eq 'APPROVED') "estimate approved at $($approved.approvedAt)"

$locked = Invoke-GraphQL $EstimatesUrl $alice $addLine @{ input = @{ estimateId = $estimate.id; expectedVersion = $approved.version; description = 'Late change'; quantity = 1; unitPrice = 1 } }
Assert-True ($locked.data.addLineItem.errors[0].code -eq 'ESTIMATE_NOT_EDITABLE') 'approved estimate rejects edits with ESTIMATE_NOT_EDITABLE'

Write-Host "`n6. Export through the MOCK accounting provider" -ForegroundColor Cyan
$first = Invoke-GraphQL $AccountingUrl $alice $export @{ input = @{ estimateId = $estimate.id } }
$exported = $first.data.exportEstimate.export
Assert-True ($exported.status -eq 'SUCCEEDED') "export $($exported.id) SUCCEEDED with reference $($exported.externalReference)"

$second = Invoke-GraphQL $AccountingUrl $alice $export @{ input = @{ estimateId = $estimate.id } }
Assert-True ($second.data.exportEstimate.export.id -eq $exported.id -and $second.data.exportEstimate.export.externalReference -eq $exported.externalReference) 'repeating the export returns the same export and reference (idempotent)'

Write-Host "`n7. Query export status" -ForegroundColor Cyan
$status = Invoke-GraphQL $AccountingUrl $alice 'query($id: UUID!) { estimateExport(estimateId: $id) { id status externalReference exportedTotal currencyCode attemptCount completedAt } }' @{ id = $estimate.id }
$s = $status.data.estimateExport
Assert-True ($s.status -eq 'SUCCEEDED' -and $s.attemptCount -eq 1) "status $($s.status), $($s.exportedTotal) $($s.currencyCode), attempts $($s.attemptCount), completed $($s.completedAt)"

$bobStatus = Invoke-GraphQL $AccountingUrl $bob 'query($id: UUID!) { estimateExport(estimateId: $id) { id } }' @{ id = $estimate.id }
Assert-True ($null -eq $bobStatus.data.estimateExport) "bob cannot see alice's export"

Write-Host "`nWalkthrough completed successfully." -ForegroundColor Green
