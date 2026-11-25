<#
  Builds portable single-file executables for a GitHub release.

  Output: dist\v<version>\BatteryHub-<version>-win-<arch>.exe (+ .sha256)
  The in-app updater looks for exactly these asset names on the latest release.
#>
param(
    [string[]]$Runtimes = @("win-x64", "win-arm64")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version.Trim()
$dist = Join-Path $root "dist\v$version"
New-Item -ItemType Directory -Force $dist | Out-Null

Write-Host "Running tests..."
dotnet run --project (Join-Path $root "tests\BatteryHub.Core.Tests") -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($rid in $Runtimes) {
    $staging = Join-Path $root "publish\$rid"
    Write-Host "Publishing $rid..."
    dotnet publish (Join-Path $root "src\BatteryHub.App\BatteryHub.App.csproj") -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o $staging --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $name = "BatteryHub-$version-$rid.exe"
    $target = Join-Path $dist $name
    Copy-Item (Join-Path $staging "BatteryHub.exe") $target -Force
    $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$target.sha256" -Value "$hash  $name" -Encoding ascii -NoNewline
    Write-Host ("  {0}  ({1:N1} MB)" -f $name, ((Get-Item $target).Length / 1MB))
}

Write-Host "Ready: $dist"
