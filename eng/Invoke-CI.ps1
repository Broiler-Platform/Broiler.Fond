[CmdletBinding()]
param(
    [ValidateSet("Full", "Kernel", "MultiPlatform")]
    [string] $Scope = "Full"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solution = Join-Path $repositoryRoot "Broiler.Fond.slnx"
$desktopHostProject = Join-Path $repositoryRoot "src/Broiler.Fond.Host.Desktop/Broiler.Fond.Host.Desktop.csproj"
$testProject = Join-Path $repositoryRoot "tests/Broiler.Fond.Kernel.Tests/Broiler.Fond.Kernel.Tests.csproj"
$presentationTestProject = Join-Path $repositoryRoot "tests/Broiler.Fond.Presentation.Tests/Broiler.Fond.Presentation.Tests.csproj"
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

function Invoke-DesktopHostSmoke {
    $arguments = @(
        "run", "--project", $desktopHostProject,
        "--configuration", "Release", "--no-build", "--no-restore",
        "--", "--smoke"
    )
    $actualLines = @(& dotnet @arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "The desktop host smoke test failed with exit code $LASTEXITCODE."
    }

    $expectedLines = @(
        "Broiler Fond - Finance on Demand - Cross-Platform Desktop Host (BUI-05)",
        "Platform qualification preflight verified."
    )
    if ($actualLines.Count -ne $expectedLines.Count) {
        throw "The desktop host smoke test produced unexpected line count."
    }
    for ($index = 0; $index -lt $expectedLines.Count; $index++) {
        if ([string] $actualLines[$index] -cne $expectedLines[$index]) {
            throw "The desktop host smoke test produced unexpected output: '$($actualLines[$index])' vs '$($expectedLines[$index])'."
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
        Invoke-DesktopHostSmoke
    }
    else {
        Invoke-DotNet -ArgumentList @("restore", $testProject, "--configfile", $nugetConfig)
        Invoke-DotNet -ArgumentList @("build", $testProject, "--configuration", "Release", "--no-restore")
    }

    Invoke-DotNet -ArgumentList @("run", "--project", $testProject, "--configuration", "Release", "--no-build", "--no-restore")
    if ($Scope -eq "Full") {
        Invoke-DotNet -ArgumentList @("run", "--project", $desktopHostProject, "--configuration", "Release", "--no-build", "--no-restore", "--", "--self-test")
        Invoke-DotNet -ArgumentList @("run", "--project", $presentationTestProject, "--configuration", "Release", "--no-build", "--no-restore")
    }
}
finally {
    Pop-Location
}
