param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version.Trim()
$outDir = Join-Path $root "releases\v$version\"

Write-Host "Building Battery Hub v$version -> $outDir"

dotnet build (Join-Path $root "tests\BatteryHub.Core.Tests\BatteryHub.Core.Tests.csproj") -c $Configuration --no-restore --nologo
& (Join-Path $root "tests\BatteryHub.Core.Tests\bin\$Configuration\net10.0-windows\BatteryHub.Core.Tests.exe")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $root "src\BatteryHub.App\BatteryHub.App.csproj") -c $Configuration --no-restore --nologo -p:OutputPath="$outDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Ready: $(Join-Path $outDir 'BatteryHub.exe')"
