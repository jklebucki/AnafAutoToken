param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "publish\AnafAutoToken.Manager"
)

[Console]::InputEncoding  = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src\AnafAutoToken.Manager\AnafAutoToken.Manager.csproj"
$iconPath = Join-Path $repoRoot "scripts\autoanaf.ico"

if (-not (Test-Path $projectPath)) {
    Write-Error "Nie znaleziono projektu menedżera: $projectPath"
    exit 1
}

if (-not (Test-Path $iconPath)) {
    Write-Error "Nie znaleziono pliku ikony: $iconPath"
    exit 1
}

if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $publishPath = $OutputPath
}
else {
    $publishPath = Join-Path $repoRoot $OutputPath
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "Nie znaleziono polecenia 'dotnet'. Zainstaluj .NET SDK 8."
    exit 1
}

$stagingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("AnafAutoToken.Manager." + [Guid]::NewGuid().ToString("N"))

Write-Host "========================================"
Write-Host "Publikacja AnafAutoToken.Manager"
Write-Host "========================================"
Write-Host ""
Write-Host "Projekt      : $projectPath"
Write-Host "Konfiguracja : $Configuration"
Write-Host "Runtime      : $Runtime"
Write-Host "Output       : $publishPath"
Write-Host "Staging      : $stagingPath"
Write-Host ""

New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

try {
    dotnet publish `
        $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:ApplicationIcon="$iconPath" `
        -o $stagingPath

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Błąd publikacji menedżera."
        exit 1
    }

    $stagedExePath = Join-Path $stagingPath "AnafAutoToken.Manager.exe"

    if (-not (Test-Path $stagedExePath)) {
        Write-Error "Publikacja zakończyła się bez wygenerowania pliku exe: $stagedExePath"
        exit 1
    }

    $exePath = Join-Path $publishPath "AnafAutoToken.Manager.exe"
    Copy-Item -Path $stagedExePath -Destination $exePath -Force
}
finally {
    if (Test-Path $stagingPath) {
        Remove-Item -Path $stagingPath -Recurse -Force
    }
}

Write-Host ""
Write-Host "========================================"
Write-Host "ZAKOŃCZONO"
Write-Host "========================================"
Write-Host ""
Write-Host "Plik EXE: $exePath" -ForegroundColor Green
Write-Host ""
Write-Host "Uwaga: Umieść ten plik w katalogu instalacyjnym serwisu (obok appsettings.json)."
Write-Host "Zapis konfiguracji wymaga uprawnień do zapisu w tym katalogu - uruchom jako Administrator,"
Write-Host "jeśli serwis jest zainstalowany w Program Files."
