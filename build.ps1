param(
    [switch]$Headless,
    [switch]$SelfContained,
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\release')
)
$ErrorActionPreference = 'Stop'
function Run-Dotnet {
    param([string[]]$Arguments)
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$previousTestDirectory = $env:GLOWSYNC_NEXT_DATA_DIR
Push-Location $PSScriptRoot
try {
    Run-Dotnet -Arguments @('build', 'GlowSync.sln', '-c', 'Release', '--nologo')
    $env:GLOWSYNC_NEXT_DATA_DIR = Join-Path $PSScriptRoot 'artifacts\test-data'
    $testArguments = @('run', '--project', 'tests', '-c', 'Release', '--no-build')
    if ($Headless) { $testArguments += @('--', '--no-desktop') }
    Run-Dotnet -Arguments $testArguments
    Run-Dotnet -Arguments @('run', '--project', 'tests', '-c', 'Release', '--no-build', '--', '--ui-smoke')
    $version = ([xml](Get-Content -Raw 'src\GlowSync.csproj')).Project.PropertyGroup.Version
    $packageName = "GlowSyncNext-$version-win-x64" + $(if ($SelfContained) { '-selfcontained' } else { '' })
    $publish = Join-Path $OutputDirectory $packageName
    Run-Dotnet -Arguments @('publish', 'src\GlowSync.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none', '-o', $publish)
    foreach ($file in @('LICENSE', 'GUIDE.ru.md', 'RELEASE-NOTES.md', 'THIRD-PARTY.md', 'TEST-REPORT.md')) { Copy-Item -LiteralPath $file -Destination $publish -Force }
    Write-Output "Ready to run: $publish"
}
finally {
    Pop-Location
    if ($null -eq $previousTestDirectory) { Remove-Item Env:\GLOWSYNC_NEXT_DATA_DIR -ErrorAction SilentlyContinue }
    else { $env:GLOWSYNC_NEXT_DATA_DIR = $previousTestDirectory }
}
