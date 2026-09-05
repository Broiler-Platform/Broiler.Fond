[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "true"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$kernelRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRootPath "src/Broiler.Fond.Kernel"))
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string] $Message)
    [void] $failures.Add($Message)
}

function Get-EvaluatedProperty {
    param($Evaluation, [string] $Name)
    $property = $Evaluation.Properties.PSObject.Properties[$Name]
    if ($null -eq $property) { return "" }
    return [string] $property.Value
}

function Get-EvaluatedItems {
    param($Evaluation, [string] $Name)
    $property = $Evaluation.Items.PSObject.Properties[$Name]
    if ($null -eq $property) { return @() }
    return @($property.Value)
}

function Get-ItemMetadata {
    param($Item, [string] $Name)
    $property = $Item.PSObject.Properties[$Name]
    if ($null -eq $property) { return "" }
    return [string] $property.Value
}

if (-not (Test-Path -LiteralPath $kernelRoot -PathType Container)) {
    throw "Kernel root does not exist: $kernelRoot"
}

$projects = @(Get-ChildItem -LiteralPath $kernelRoot -Recurse -File -Filter "*.csproj")
if ($projects.Count -eq 0) {
    throw "No kernel project was found below $kernelRoot."
}

$kernelProjectPaths = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects) {
    [void] $kernelProjectPaths.Add([System.IO.Path]::GetFullPath($project.FullName))
}

foreach ($project in $projects) {
    $projectPath = [System.IO.Path]::GetFullPath($project.FullName)
    $relativeProject = [System.IO.Path]::GetRelativePath($repositoryRootPath, $projectPath)
    [xml] $projectXml = Get-Content -LiteralPath $projectPath -Raw

    if ($projectXml.Project.GetAttribute("Sdk") -ne "Microsoft.NET.Sdk") {
        Add-Failure "$relativeProject must use only the in-box Microsoft.NET.Sdk."
    }
    if (@($projectXml.SelectNodes("//*[local-name()='Sdk']")).Count -ne 0) {
        Add-Failure "$relativeProject contains a nested SDK reference."
    }
    if (@($projectXml.SelectNodes("//*[local-name()='Import']")).Count -ne 0) {
        Add-Failure "$relativeProject contains an explicit MSBuild import."
    }

    $arguments = @(
        "msbuild", $projectPath, "-nologo",
        "-getProperty:BroilerFondKernel,TargetFramework,TargetFrameworks,TargetPlatformIdentifier,TargetPlatformVersion,SupportedOSPlatformVersion,UseWPF,UseWindowsForms,UseMaui,RuntimeIdentifier,RuntimeIdentifiers,EnableComHosting",
        "-getItem:PackageReference,PackageDownload,ProjectReference,NativeReference,COMReference,FrameworkReference,Reference,Analyzer"
    )
    $rawEvaluation = @(& dotnet @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        Add-Failure "$relativeProject could not be evaluated by MSBuild: $($rawEvaluation -join ' ')"
        continue
    }

    try {
        $evaluation = ConvertFrom-Json -InputObject ($rawEvaluation -join [Environment]::NewLine)
    }
    catch {
        Add-Failure "$relativeProject returned unreadable MSBuild evaluation output: $($_.Exception.Message)"
        continue
    }

    if ((Get-EvaluatedProperty $evaluation "BroilerFondKernel").Trim() -ine "true") {
        Add-Failure "$relativeProject must set BroilerFondKernel=true."
    }

    $targetFrameworkText = Get-EvaluatedProperty $evaluation "TargetFrameworks"
    if ([string]::IsNullOrWhiteSpace($targetFrameworkText)) {
        $targetFrameworkText = Get-EvaluatedProperty $evaluation "TargetFramework"
    }
    $targetFrameworks = @($targetFrameworkText -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($targetFrameworks.Count -eq 0) {
        Add-Failure "$relativeProject has no target framework."
    }
    foreach ($targetFramework in $targetFrameworks) {
        $match = [regex]::Match($targetFramework.Trim(), "^net(?<major>[0-9]+)[.]0$")
        if (-not $match.Success -or [int] $match.Groups["major"].Value -lt 10) {
            Add-Failure "$relativeProject targets '$targetFramework'; kernel TFMs must be platform-neutral net10.0 or newer."
        }
    }

    foreach ($propertyName in @("TargetPlatformIdentifier", "TargetPlatformVersion", "SupportedOSPlatformVersion", "RuntimeIdentifier", "RuntimeIdentifiers")) {
        $value = Get-EvaluatedProperty $evaluation $propertyName
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            Add-Failure "$relativeProject sets $propertyName='$value'."
        }
    }
    foreach ($propertyName in @("UseWPF", "UseWindowsForms", "UseMaui", "EnableComHosting")) {
        if ((Get-EvaluatedProperty $evaluation $propertyName).Trim() -ieq "true") {
            Add-Failure "$relativeProject enables $propertyName."
        }
    }

    foreach ($itemName in @("PackageReference", "PackageDownload", "NativeReference", "COMReference", "Reference")) {
        foreach ($item in @(Get-EvaluatedItems $evaluation $itemName)) {
            $identity = Get-ItemMetadata $item "Identity"
            $definedBy = Get-ItemMetadata $item "DefiningProjectFullPath"
            Add-Failure "$relativeProject has $itemName '$identity' (defined by '$definedBy')."
        }
    }

    foreach ($framework in @(Get-EvaluatedItems $evaluation "FrameworkReference")) {
        $identity = Get-ItemMetadata $framework "Identity"
        $implicit = Get-ItemMetadata $framework "IsImplicitlyDefined"
        if ($identity -ine "Microsoft.NETCore.App" -or $implicit -ine "true") {
            Add-Failure "$relativeProject has non-BCL FrameworkReference '$identity'."
        }
    }

    foreach ($analyzer in @(Get-EvaluatedItems $evaluation "Analyzer")) {
        if ((Get-ItemMetadata $analyzer "IsImplicitlyDefined") -ine "true") {
            Add-Failure "$relativeProject has a non-SDK analyzer '$((Get-ItemMetadata $analyzer 'Identity'))'."
        }
    }

    foreach ($reference in @(Get-EvaluatedItems $evaluation "ProjectReference")) {
        $referencedPath = Get-ItemMetadata $reference "FullPath"
        if ([string]::IsNullOrWhiteSpace($referencedPath)) {
            $referencedPath = [System.IO.Path]::GetFullPath(
                (Join-Path $project.DirectoryName (Get-ItemMetadata $reference "Identity")))
        }
        else {
            $referencedPath = [System.IO.Path]::GetFullPath($referencedPath)
        }

        if (-not $kernelProjectPaths.Contains($referencedPath)) {
            Add-Failure "$relativeProject references a project outside the kernel boundary: $referencedPath"
        }
        if ($referencedPath -match "(?i)(?:^|[\\/._-])(host|ui|windows|win32|linux|macos|osx|android|ios|maui|native)(?:[\\/._-]|$)") {
            Add-Failure "$relativeProject references a host, UI, platform, or native project: $referencedPath"
        }
        if (-not [string]::IsNullOrWhiteSpace((Get-ItemMetadata $reference "OutputItemType"))) {
            Add-Failure "$relativeProject uses a non-standard ProjectReference output type for $referencedPath."
        }
    }
}

$sourceRules = [ordered]@{
    "platform/UI namespace" = "(?i)\b(?:Microsoft[.]Win32|Microsoft[.]UI|System[.]Windows|System[.]Management|System[.]Drawing|Windows[.])\b"
    "native interop" = "(?i)\b(?:DllImport|LibraryImport|ComImport)(?:Attribute)?\b|\bNativeLibrary[.]"
    "platform annotation" = "(?i)\b(?:SupportedOSPlatform|UnsupportedOSPlatform)(?:Attribute)?\b"
    "platform branch" = "(?i)^\s*#(?:if|elif)\b.*\b(?:WINDOWS|WIN32|LINUX|OSX|ANDROID|IOS|MACCATALYST|BROWSER)\b"
    "runtime OS branch" = "(?i)\bOperatingSystem[.]Is(?:Windows|Linux|MacOS|Android|IOS|MacCatalyst|TvOS|WatchOS|Browser)\s*[(]"
}

foreach ($sourceFile in @(Get-ChildItem -LiteralPath $kernelRoot -Recurse -File -Filter "*.cs")) {
    $relativeSource = [System.IO.Path]::GetRelativePath($kernelRoot, $sourceFile.FullName)
    if ($relativeSource -match "(?i)(^|[\\/])(?:bin|obj)([\\/]|$)") { continue }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $sourceFile.FullName) {
        $lineNumber++
        foreach ($rule in $sourceRules.GetEnumerator()) {
            if ($line -match $rule.Value) {
                Add-Failure "$relativeSource`:$lineNumber violates the kernel $($rule.Key) rule."
            }
        }
    }
}

$forbiddenExtensions = @(".dll", ".exe", ".so", ".dylib", ".a", ".lib", ".xaml", ".axaml", ".razor", ".cshtml")
foreach ($file in @(Get-ChildItem -LiteralPath $kernelRoot -Recurse -File)) {
    $relativeFile = [System.IO.Path]::GetRelativePath($kernelRoot, $file.FullName)
    if ($relativeFile -match "(?i)(^|[\\/])(?:bin|obj)([\\/]|$)") { continue }
    if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) {
        Add-Failure "$relativeFile is not allowed inside the platform-neutral kernel."
    }
}

if ($failures.Count -ne 0) {
    foreach ($failure in @($failures | Sort-Object -Unique)) {
        [Console]::Error.WriteLine("ERROR: $failure")
    }
    throw "Kernel boundary verification failed with $($failures.Count) finding(s)."
}

Write-Host "Kernel boundary verified for $($projects.Count) project(s)."
