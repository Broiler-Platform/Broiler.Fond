[CmdletBinding()]
param(
    [ValidateSet("Full", "Kernel")]
    [string] $Scope = "Full"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solution = Join-Path $repositoryRoot "Broiler.Fond.slnx"
$windowsHostProject = Join-Path $repositoryRoot "src/Broiler.Fond.Host.Windows/Broiler.Fond.Host.Windows.csproj"
$testProject = Join-Path $repositoryRoot "tests/Broiler.Fond.Kernel.Tests/Broiler.Fond.Kernel.Tests.csproj"
$nugetConfig = Join-Path $repositoryRoot "NuGet.config"
$boundaryCheck = Join-Path $PSScriptRoot "Verify-KernelBoundary.ps1"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "true"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $ArgumentList)

    & dotnet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-WindowsHostSmoke {
    $arguments = @(
        "run", "--project", $windowsHostProject,
        "--configuration", "Release", "--no-build", "--no-restore"
    )
    $actualLines = @(& dotnet @arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "The inert Windows host failed with exit code $LASTEXITCODE."
    }

    $expectedLines = @(
        "Broiler Fond - Finance on Demand - Milestone 0 skeleton",
        "Banking, storage, credential, UI, and network operations are not implemented."
    )
    if ($actualLines.Count -ne $expectedLines.Count) {
        throw "The inert Windows host produced unexpected output."
    }
    for ($index = 0; $index -lt $expectedLines.Count; $index++) {
        if ([string] $actualLines[$index] -cne $expectedLines[$index]) {
            throw "The inert Windows host produced unexpected output."
        }
    }

    $actualLines | ForEach-Object { Write-Host $_ }
}

Push-Location $repositoryRoot
try {
    Invoke-DotNet -ArgumentList @("--version")
    & $boundaryCheck -RepositoryRoot $repositoryRoot

    if ($Scope -eq "Full") {
        Invoke-DotNet -ArgumentList @("restore", $solution, "--configfile", $nugetConfig)
        Invoke-DotNet -ArgumentList @("format", $solution, "--no-restore", "--verify-no-changes", "--verbosity", "minimal")
        Invoke-DotNet -ArgumentList @("build", $solution, "--configuration", "Release", "--no-restore")
        Invoke-WindowsHostSmoke
    }
    else {
        Invoke-DotNet -ArgumentList @("restore", $testProject, "--configfile", $nugetConfig)
        Invoke-DotNet -ArgumentList @("build", $testProject, "--configuration", "Release", "--no-restore")
    }

    Invoke-DotNet -ArgumentList @("run", "--project", $testProject, "--configuration", "Release", "--no-build", "--no-restore")
}
finally {
    Pop-Location
}
