<#
.SYNOPSIS
    Instaluje AnafAutoToken jako usługę Windows.

.DESCRIPTION
    Dwa tryby pracy:

    1) Bez -ArtifactPath - skrypt sam publikuje workera (wymaga SDK .NET 10 na tej maszynie).
    2) Z -ArtifactPath   - skrypt kopiuje gotową paczkę zbudowaną gdzie indziej.
       Wtedy na maszynie docelowej nie jest potrzebne ani SDK, ani runtime .NET,
       bo opublikowany EXE jest samowystarczalny (self-contained, single file).

.PARAMETER ArtifactPath
    Ścieżka do gotowego AnafAutoToken.Worker.exe albo do katalogu z paczką
    wygenerowaną przez scripts\publish-worker-single-file.ps1.
#>
param(
    [string]$ServiceName = "AnafAutoToken",
    [string]$DisplayName = "ANAF Auto Token Refresh Service",
    [string]$Description = "Automatycznie odświeża tokeny ANAF przed wygaśnięciem",
    [string]$ArtifactPath
)

[Console]::InputEncoding  = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# =======================================================
# SPRAWDZENIE UPRAWNIEŃ ADMINISTRATORA
# =======================================================
$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Uruchom ten skrypt jako Administrator."
    exit 1
}

Write-Host "========================================"
Write-Host "Instalacja serwisu AnafAutoToken"
Write-Host "========================================"
Write-Host ""

# =======================================================
# TRYB PRACY
# =======================================================
$installAsService = Read-Host "Czy zainstalować jako serwis? (Y/N)"

# =======================================================
# ŚCIEŻKI
# =======================================================
$configFolder = Read-Host "Podaj ścieżkę do folderu z config.ini"
$configFilePath = Join-Path $configFolder "config.ini"
if (-not (Test-Path $configFilePath)) {
    Write-Warning "Plik config.ini nie istnieje w podanym folderze: $configFolder"
}

$BackupPath   = Join-Path $configFolder "backups"

$installFolder = Read-Host "Podaj ścieżkę do folderu instalacji"
$publishPath   = $installFolder
$LogPath       = Join-Path $installFolder "logs"

New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

# =======================================================
# ŹRÓDŁO PLIKÓW APLIKACJI
# =======================================================
if ($ArtifactPath) {
    # --- Tryb 1: gotowa paczka, host nie potrzebuje .NET -------------------
    Write-Host "Instalacja z gotowej paczki: $ArtifactPath"

    if (-not (Test-Path $ArtifactPath)) {
        Write-Error "Nie znaleziono wskazanej paczki: $ArtifactPath"
        exit 1
    }

    if (Test-Path $ArtifactPath -PathType Container) {
        if (-not (Test-Path (Join-Path $ArtifactPath "AnafAutoToken.Worker.exe"))) {
            Write-Error "W katalogu $ArtifactPath nie ma pliku AnafAutoToken.Worker.exe."
            exit 1
        }

        Copy-Item -Path (Join-Path $ArtifactPath "*") -Destination $publishPath -Recurse -Force
    }
    else {
        Copy-Item -Path $ArtifactPath -Destination (Join-Path $publishPath "AnafAutoToken.Worker.exe") -Force
        Write-Warning "Skopiowano sam plik EXE. Upewnij się, że w $publishPath są też appsettings.json i katalog EmailTemplates."
    }

    Write-Host "Pliki skopiowane do: $publishPath"
}
else {
    # --- Tryb 2: publikacja na miejscu, wymaga SDK .NET 10 -----------------
    Write-Host "Sprawdzanie SDK .NET 10..."

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "Nie znaleziono polecenia 'dotnet'. Zainstaluj SDK .NET 10 albo uruchom skrypt z -ArtifactPath <gotowa paczka>."
        exit 1
    }

    $hasSdk10 = (dotnet --list-sdks) | Where-Object { $_ -match '^10\.' }

    if (-not $hasSdk10) {
        Write-Error "Nie znaleziono SDK .NET 10. Pobierz z https://dotnet.microsoft.com/download/dotnet/10.0 albo uruchom skrypt z -ArtifactPath <gotowa paczka>."
        exit 1
    }

    Write-Host "SDK .NET 10 OK"
    Write-Host ""

    & (Join-Path $PSScriptRoot "publish-worker-single-file.ps1") -OutputPath $publishPath

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Błąd publikacji aplikacji."
        exit 1
    }
}

$binaryFullPath = Join-Path $publishPath "AnafAutoToken.Worker.exe"

if (-not (Test-Path $binaryFullPath)) {
    Write-Error "Nie znaleziono pliku exe: $binaryFullPath"
    exit 1
}

# =======================================================
# KATALOGI
# =======================================================
if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
}

if (-not (Test-Path $LogPath)) {
    New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
}

# =======================================================
# INSTALACJA SERWISU
# =======================================================
if ($installAsService -eq "Y" -or $installAsService -eq "y") {

    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($existingService) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep 3
    }

    New-Service `
        -Name $ServiceName `
        -BinaryPathName $binaryFullPath `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic

    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

    Start-Service $ServiceName
    Start-Sleep 3

    $service = Get-Service $ServiceName
}

# =======================================================
# PODSUMOWANIE
# =======================================================
Write-Host ""
Write-Host "========================================"
Write-Host "ZAKOŃCZONO"
Write-Host "========================================"
Write-Host ""

if ($service) {
    if ($service.Status -eq "Running") {
        $color = "Green"
    } else {
        $color = "Yellow"
    }

    Write-Host "Nazwa serwisu: $ServiceName"
    Write-Host "Status       : $($service.Status)" -ForegroundColor $color
}

Write-Host ""
Write-Host "Aplikacja : $publishPath"
Write-Host "Backupy   : $BackupPath"
Write-Host "Logi      : $LogPath"
Write-Host ""
Write-Host "Runtime .NET jest wbudowany w AnafAutoToken.Worker.exe - host nie wymaga instalacji .NET." -ForegroundColor Green
Write-Host ""

if ($installAsService -ne "Y" -and $installAsService -ne "y") {
    Write-Host "Uruchom ręcznie:"
    Write-Host "$publishPath\AnafAutoToken.Worker.exe"
}
