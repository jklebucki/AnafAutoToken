<#
.SYNOPSIS
    Publikuje wybrany projekt jako pojedynczy, samowystarczalny plik wykonywalny.

.DESCRIPTION
    Publikacja jest self-contained: runtime .NET 10 (oraz ASP.NET Core dla workera)
    jest wkompilowany w plik EXE razem z bibliotekami natywnymi SQLite. Na maszynie
    docelowej NIE trzeba instalować ani SDK, ani runtime'u .NET.

    SDK .NET 10 jest potrzebne wyłącznie na maszynie, na której uruchamiasz ten skrypt.

.PARAMETER Project
    Worker   - usługa odświeżająca tokeny (kopiowany jest cały katalog: exe,
               appsettings.json, EmailTemplates, skrypty rejestracji usługi).
    Exporter - narzędzie CLI do eksportu tokenów (kopiowany jest sam plik exe).
    Manager  - okienkowy menedżer (Windows-only, kopiowany jest sam plik exe).
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Worker", "Exporter", "Manager")]
    [string]$Project,

    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

[Console]::InputEncoding  = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$definitions = @{
    Worker   = @{ AssemblyName = "AnafAutoToken.Worker";   CopyEntireOutput = $true;  WindowsOnly = $false }
    Exporter = @{ AssemblyName = "AnafAutoToken.Exporter"; CopyEntireOutput = $false; WindowsOnly = $false }
    Manager  = @{ AssemblyName = "AnafAutoToken.Manager";  CopyEntireOutput = $false; WindowsOnly = $true }
}

$definition = $definitions[$Project]
$assemblyName = $definition.AssemblyName
$isWindowsRuntime = $Runtime -like "win-*"
$executableName = if ($isWindowsRuntime) { "$assemblyName.exe" } else { $assemblyName }

if ($definition.WindowsOnly -and -not $isWindowsRuntime) {
    Write-Error "Projekt $Project jest aplikacją WinForms i można go publikować tylko dla runtime win-*. Podano: $Runtime"
    exit 1
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src\$assemblyName\$assemblyName.csproj"
$iconPath = Join-Path $repoRoot "scripts\autoanaf.ico"

if (-not (Test-Path $projectPath)) {
    Write-Error "Nie znaleziono projektu: $projectPath"
    exit 1
}

if (-not (Test-Path $iconPath)) {
    Write-Error "Nie znaleziono pliku ikony: $iconPath"
    exit 1
}

if (-not $OutputPath) {
    $OutputPath = "publish\$assemblyName"
}

$publishPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }

# ---------------------------------------------------------------------------
# Wymagania maszyny budującej (maszyna docelowa nie potrzebuje niczego)
# ---------------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "Nie znaleziono polecenia 'dotnet'. Do zbudowania paczki potrzebne jest SDK .NET 10 na TEJ maszynie."
    exit 1
}

$hasSdk10 = (dotnet --list-sdks) | Where-Object { $_ -match '^10\.' }

if (-not $hasSdk10) {
    Write-Error "Nie znaleziono SDK .NET 10 na tej maszynie. Pobierz z https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
}

$stagingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("$assemblyName." + [Guid]::NewGuid().ToString("N"))

Write-Host "========================================"
Write-Host "Publikacja $assemblyName (single file, self-contained)"
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
        -p:SatelliteResourceLanguages=en `
        -p:DebugType=none `
        -p:ApplicationIcon="$iconPath" `
        -o $stagingPath

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Błąd publikacji projektu $assemblyName."
        exit 1
    }

    $stagedExecutablePath = Join-Path $stagingPath $executableName

    if (-not (Test-Path $stagedExecutablePath)) {
        Write-Error "Publikacja zakończyła się bez wygenerowania pliku wykonywalnego: $stagedExecutablePath"
        exit 1
    }

    if ($definition.CopyEntireOutput) {
        # Worker potrzebuje obok siebie appsettings.json i katalogu EmailTemplates -
        # pliki treści nie są pakowane do single file.
        Copy-Item -Path (Join-Path $stagingPath "*") -Destination $publishPath -Recurse -Force
    }
    else {
        Copy-Item -Path $stagedExecutablePath -Destination (Join-Path $publishPath $executableName) -Force
    }

    $executablePath = Join-Path $publishPath $executableName
    $sizeMb = [math]::Round((Get-Item $executablePath).Length / 1MB, 1)
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
Write-Host "Plik wykonywalny: $executablePath ($sizeMb MB)" -ForegroundColor Green
Write-Host "Runtime .NET 10 jest wbudowany - maszyna docelowa nie wymaga instalacji .NET." -ForegroundColor Green
Write-Host ""

if ($definition.CopyEntireOutput) {
    Write-Host "Obok pliku EXE skopiowano appsettings.json, katalog EmailTemplates oraz skrypty rejestracji usługi."
}
else {
    Write-Host "Umieść ten plik w katalogu instalacyjnym serwisu (obok appsettings.json i tokens.db)."
}
