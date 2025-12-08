param(
    [string]$ServiceName = "AnafAutoToken",
    [string]$DisplayName = "ANAF Auto Token Refresh Service",
    [string]$Description = "Automatycznie odświeża tokeny ANAF przed wygaśnięciem"
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

# =======================================================
# SPRAWDZENIE .NET 8
# =======================================================
Write-Host "Sprawdzanie .NET 8..."

$dotnetVersion = dotnet --list-runtimes | Select-String "Microsoft.NETCore.App 8.0"
if (-not $dotnetVersion) {
    Write-Error ".NET 8.0 Runtime nie jest zainstalowany."
    exit 1
}

Write-Host ".NET 8 OK"
Write-Host ""

# =======================================================
# PUBLIKACJA APLIKACJI
# =======================================================
dotnet publish `
"$PSScriptRoot\..\src\AnafAutoToken.Worker\AnafAutoToken.Worker.csproj" `
-c Release `
-r win-x64 `
--self-contained true `
-p:PublishSingleFile=true `
-p:ApplicationIcon="..\..\scripts\autoanaf.ico" `
-o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd publikacji aplikacji."
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

    $binaryFullPath = Join-Path $publishPath "AnafAutoToken.Worker.exe"

    if (-not (Test-Path $binaryFullPath)) {
        Write-Error "Nie znaleziono pliku exe."
        exit 1
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

if ($installAsService -ne "Y" -and $installAsService -ne "y") {
    Write-Host "Uruchom ręcznie:"
    Write-Host "$publishPath\AnafAutoToken.Worker.exe"
}
