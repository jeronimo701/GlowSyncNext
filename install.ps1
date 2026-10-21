# Builds GlowSync and installs it to %LOCALAPPDATA%\Programs\GlowSync (no admin rights needed).
# Settings live separately in %LOCALAPPDATA%\GlowSync and survive reinstalls.
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\GlowSyncNext'
$exe = Join-Path $target 'GlowSync.Next.exe'
$out = Join-Path $PSScriptRoot 'artifacts\publish'

dotnet publish (Join-Path $PSScriptRoot 'src\GlowSync.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

if (Get-Process 'GlowSync.Next' -ErrorAction SilentlyContinue) {
    # Ask the running copy to turn the strip off and quit; force only if it does not respond.
    & $exe --exit
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Process 'GlowSync.Next' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (Get-Process 'GlowSync.Next' -ErrorAction SilentlyContinue) { throw 'Close GlowSync Next before installing.' }
}

New-Item -ItemType Directory -Force $target | Out-Null
# The file handle can outlive the process by a moment.
for ($attempt = 1; ; $attempt++) {
    try {
        Copy-Item (Join-Path $out 'GlowSync.Next.exe') $exe -Force
        break
    }
    catch [System.IO.IOException] {
        if ($attempt -ge 20) { throw }
        Start-Sleep -Milliseconds 500
    }
}

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'GlowSync Next.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'Подсветка монитора'
$shortcut.Save()

Write-Host "GlowSync Next установлен: $exe"
Write-Host 'Start after closing the other LED applications. Autostart is opt-in.'
