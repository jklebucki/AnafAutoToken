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
$ConfigPath   = Join-Path $configFolder "config.ini"
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
-p:ApplicationIcon="autoanaf.ico" `
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
# CONFIG.INI
# =======================================================
if (-not (Test-Path $ConfigPath)) {
    "[AccessToken]"           | Out-File $ConfigPath -Encoding UTF8
    "token=dummy"             | Out-File $ConfigPath -Append -Encoding UTF8
    "refresh_token=dummy"     | Out-File $ConfigPath -Append -Encoding UTF8
}

# =======================================================
# CREDENTIALS
# =======================================================
# $username     = Read-Host "Podaj username"
# $password     = Read-Host "Podaj password"
# $refreshToken = Read-Host "Podaj refresh_token"

# =======================================================
# APPSETTINGS.JSON
# =======================================================
# $appsettingsPath = Join-Path $publishPath "appsettings.json"

# if (Test-Path $appsettingsPath) {

#     $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json

#     $appsettings.Anaf.BasicAuth.Username   = $username
#     $appsettings.Anaf.BasicAuth.Password   = $password
#     $appsettings.Anaf.InitialRefreshToken  = $refreshToken
#     $appsettings.Anaf.ConfigFilePath       = $ConfigPath
#     $appsettings.Anaf.BackupDirectory      = $BackupPath

#     $appsettings | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath -Encoding UTF8
# }

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
Write-Host "Config    : $ConfigPath"
Write-Host "Backupy   : $BackupPath"
Write-Host "Logi      : $LogPath"
Write-Host ""

if ($installAsService -ne "Y" -and $installAsService -ne "y") {
    Write-Host "Uruchom ręcznie:"
    Write-Host "$publishPath\AnafAutoToken.Worker.exe"
}
