[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ArtifactDirectory,
    [Parameter(Mandatory)][string] $OutputPath
)

# Offline inventory only. This script never opens profiles, reads credentials or contacts banks.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw "ArtifactDirectory must be a directory." }
if (Test-Path -LiteralPath $OutputPath) { throw "OutputPath already exists; use a new evidence file." }
$hostAssembly = Join-Path $artifactRoot "Broiler.Fond.Host.Desktop.dll"
if (-not (Test-Path -LiteralPath $hostAssembly -PathType Leaf)) { throw "The candidate desktop host assembly is missing." }
$payload = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File |
    Where-Object { $_.Extension -in @(".dll", ".exe", ".json") } |
    Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($artifactRoot, $_.FullName).Replace("\", "/")
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$commit = & git -C $repositoryRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "Cannot identify repository HEAD." }
$changes = @(& git -C $repositoryRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw "Cannot inspect repository status." }
$record = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    status = "NotRun"
    liveRequestsSentByGenerator = $false
    candidate = [ordered]@{
        sourceCommit = "$commit".Trim()
        sourceHasLocalChanges = $changes.Count -gt 0
        immutableReleaseCandidate = $false
        operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        operatingSystemArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        payloadHashScope = "DLL, EXE and JSON files recursively; excludes native files with other extensions, signing verification and external configuration."
        files = $payload
    }
    target = [ordered]@{
        bankName = $null; bankCode = $null; environment = $null
        endpoint = $null; officialEndpointSource = $null
        productId = $null; productVersion = $null; registrationVerified = $false
        procedure = $null; segmentVersions = $null
    }
    authorization = [ordered]@{ status = "Pending"; scope = "Read-only setup, account discovery and balance retrieval"; reference = $null; authorizedAtUtc = $null }
    evidence = [ordered]@{
        deterministicCiReference = $null; liveStartedAtUtc = $null; liveCompletedAtUtc = $null
        synchronization = "NotRun"; discoveryReview = "NotRun"; firstBalance = "NotRun"
        reopenAmountAndRetrievalTime = "NotRun"; laterRefreshAfterUnlock = "NotRun"
        cancellation = "NotRun"; safeFailureFeedback = "NotRun"
        accountAndCurrencyComparison = "NotRun"; reviewerDecision = "Pending"
    }
    compatibilityClaim = "None. An offline inventory is not a successful bank qualification."
}
$json = $record | ConvertTo-Json -Depth 12
# CreateNew prevents concurrent runs from overwriting an existing evidence record.
$stream = [System.IO.File]::Open([System.IO.Path]::GetFullPath($OutputPath), [System.IO.FileMode]::CreateNew)
try {
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream.Write($bytes, 0, $bytes.Length)
}
finally { $stream.Dispose() }
Write-Output "Offline candidate inventory created. Live qualification remains NotRun."
