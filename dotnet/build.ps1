<#
.SYNOPSIS
    Builds the native library, stages it as a NuGet runtime asset, and
    optionally builds, tests, and packs the managed package.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Test
    ./build.ps1 -Pack
    ./build.ps1 -Rid win-arm64 -Target aarch64-pc-windows-msvc

.DESCRIPTION
    Repeat with different -Rid/-Target pairs before packing to produce a
    multi-platform package; the staged runtimes/ directory accumulates.
#>
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Pack,
    [string]$Rid,
    [string]$Target
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$nativeDir = Join-Path $scriptDir 'native'
$runtimesDir = Join-Path $scriptDir 'runtimes'

if (-not $Rid) {
    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        'X64' { 'x64' }
        'Arm64' { 'arm64' }
        default { throw "Unsupported architecture: $_" }
    }
    $Rid = if ($IsWindows -or $null -eq $IsWindows) { "win-$architecture" }
           elseif ($IsMacOS) { "osx-$architecture" }
           else { "linux-$architecture" }
}

$libraryName = if ($Rid -like 'win-*') { 'pdf_inspector_ffi.dll' }
               elseif ($Rid -like 'osx-*') { 'libpdf_inspector_ffi.dylib' }
               else { 'libpdf_inspector_ffi.so' }

Write-Host "==> cargo build --release (rid: $Rid$(if ($Target) { ", target: $Target" }))"
Push-Location $nativeDir
try {
    if ($Target) {
        & cargo build --release --target $Target
        $built = Join-Path $nativeDir "target/$Target/release/$libraryName"
    }
    else {
        & cargo build --release
        $built = Join-Path $nativeDir "target/release/$libraryName"
    }
    if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }
}
finally {
    Pop-Location
}

if (-not (Test-Path $built)) {
    throw "Expected native library not found: $built"
}

$destination = Join-Path $runtimesDir "$Rid/native"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item $built (Join-Path $destination $libraryName) -Force
Write-Host "==> staged $destination/$libraryName"

Write-Host '==> dotnet build'
& dotnet build (Join-Path $scriptDir 'PdfInspector.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

if ($Test) {
    Write-Host '==> dotnet test'
    $env:PDF_INSPECTOR_NATIVE_LIBRARY = $built
    & dotnet test (Join-Path $scriptDir 'PdfInspector.sln') -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed' }
}

if ($Pack) {
    Write-Host '==> dotnet pack'
    & dotnet pack (Join-Path $scriptDir 'src/PdfInspector/PdfInspector.csproj') `
        -c Release --no-build -o (Join-Path $scriptDir 'artifacts')
    if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }
    Write-Host "==> packages in $(Join-Path $scriptDir 'artifacts')"
}
