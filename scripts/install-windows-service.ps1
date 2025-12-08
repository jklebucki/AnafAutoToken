param(
    [string]$ServiceName = "AnafAutoToken",
    [string]$DisplayName = "ANAF Auto Token Refresh Service",
    [string]$Description = "Automatycznie odświeża tokeny ANAF przed wygaśnięciem"
)

# Sprawdzenie uprawnień administratora
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Ten skrypt wymaga uprawnień administratora. Uruchom PowerShell jako Administrator."
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Instalacja serwisu AnafAutoToken" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$installAsService = Read-Host "Czy chcesz zainstalować jako serwis Windows? (Y/N)"

$configFolder = Read-Host "Podaj ścieżkę do folderu z config.ini"
$ConfigPath = Join-Path $configFolder "config.ini"
$BackupPath = Join-Path $configFolder "backups"

$installFolder = Read-Host "Podaj ścieżkę do folderu instalacji"
$publishPath = Join-Path $installFolder "bin\Release\net8.0\publish"
$LogPath = Join-Path $installFolder "logs"

Write-Host "Sprawdzanie instalacji .NET 8.0 Runtime..." -ForegroundColor Yellow
$dotnetVersion = dotnet --list-runtimes | Select-String "Microsoft.NETCore.App 8.0"
if (-not $dotnetVersion) {
    Write-Error ".NET 8.0 Runtime nie jest zainstalowany"
    exit 1
}

dotnet publish `
"$PSScriptRoot\..\src\AnafAutoToken.Worker\AnafAutoToken.Worker.csproj" `
-c Release `
-r win-x64 `
--self-contained true `
-p:PublishSingleFile=true `
-o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd publikacji"
    exit 1
}

@($BackupPath, $LogPath) | ForEach-Object {
    if (-not (Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
}

if (-not (Test-Path $ConfigPath)) {
$sampleConfig = @"
[AcessToken]
token=dummy
refresh_token=dummy
"@
    $sampleConfig | Out-File $ConfigPath -Encoding UTF8
}

$username = Read-Host "Podaj username"
$password = Read-Host "Podaj password"
$refreshToken = Read-Host "Podaj refresh_token"

$appsettingsPath = Join-Path $publishPath "appsettings.json"
if (Test-Path $appsettingsPath) {
    $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    $appsettings.Anaf.BasicAuth.Username = $username
    $appsettings.Anaf.BasicAuth.Password = $password
    $appsettings.Anaf.InitialRefreshToken = $refreshToken
    $appsettings.Anaf.ConfigFilePath = $ConfigPath
    $appsettings.Anaf.BackupDirectory = $BackupPath
    $appsettings | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath -Encoding UTF8
}

if ($installAsService -eq 'Y' -or $installAsService -eq 'y') {

    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep 3
    }

    $binaryFullPath = Join-Path $publishPath "AnafAutoToken.Worker.exe"
    New-Service -Name $ServiceName `
        -BinaryPathName $binaryFullPath `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic

    sc.exe failure $ServiceName reset= 86400 actions= restart/60000 | Out-Null
    Start-Service $ServiceName
    $service = Get-Service $ServiceName
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Zakończono" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($service) {
    $color = if ($service.Status -eq 'Running') { 'Green' } else { 'Yellow' }
    Write-Host "Status: $($service.Status)" -ForegroundColor $color
}
