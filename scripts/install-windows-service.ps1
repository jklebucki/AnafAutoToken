# ============================================================================
# Script instalacji serwisu Windows dla AnafAutoToken
# ============================================================================

param(
    [string]$ServiceName = "AnafAutoToken",
    [string]$DisplayName = "ANAF Auto Token Refresh Service",
    [string]$Description = "Automatycznie odświeża tokeny ANAF przed wygaśnięciem"
)

# Sprawdzenie uprawnień administratora
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "Ten skrypt wymaga uprawnień administratora. Uruchom PowerShell jako Administrator."
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Instalacja serwisu AnafAutoToken" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Pytaj o tryb instalacji
$installAsService = Read-Host "Czy chcesz zainstalować jako serwis Windows? (Y/N)"
Write-Host ""

# Pytaj o folder z config.ini
$configFolder = Read-Host "Podaj ścieżkę do folderu z config.ini"
$ConfigPath = Join-Path $configFolder "config.ini"
$BackupPath = Join-Path $configFolder "backups"

# Pytaj o folder instalacji
$installFolder = Read-Host "Podaj ścieżkę do folderu instalacji"
$publishPath = Join-Path $installFolder "bin\Release\net8.0\publish"
$LogPath = Join-Path $installFolder "logs"

# Sprawdzenie czy .NET 8.0 Runtime jest zainstalowany
Write-Host "Sprawdzanie instalacji .NET 8.0 Runtime..." -ForegroundColor Yellow
$dotnetVersion = dotnet --list-runtimes | Select-String "Microsoft.NETCore.App 8.0"
if (-not $dotnetVersion) {
    Write-Error ".NET 8.0 Runtime nie jest zainstalowany. Pobierz z: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}
Write-Host "✓ .NET 8.0 Runtime znaleziony" -ForegroundColor Green
Write-Host ""

# Publikacja aplikacji jako pojedynczy plik exe
Write-Host "Publikowanie aplikacji jako pojedynczy plik exe..." -ForegroundColor Yellow
dotnet publish "$PSScriptRoot\..\src\AnafAutoToken.Worker\AnafAutoToken.Worker.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd podczas publikacji aplikacji"
    exit 1
}
Write-Host "✓ Aplikacja opublikowana w: $publishPath" -ForegroundColor Green
Write-Host ""

# Tworzenie katalogów
Write-Host "Tworzenie katalogów roboczych..." -ForegroundColor Yellow
@($BackupPath, $LogPath) | ForEach-Object {
    if (-not (Test-Path $_)) {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
        Write-Host "  ✓ Utworzono: $_" -ForegroundColor Green
    } else {
        Write-Host "  ✓ Istnieje: $_" -ForegroundColor Green
    }
}
Write-Host ""

# Sprawdzenie czy config.ini istnieje
if (-not (Test-Path $ConfigPath)) {
    Write-Warning "Plik config.ini nie istnieje. Tworzenie przykładowego pliku..."
    
    $sampleConfig = @"
[AcessToken]
token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
refresh_token=your_initial_refresh_token_here
"@
    $sampleConfig | Out-File -FilePath $ConfigPath -Encoding UTF8
    Write-Host "✓ Utworzono przykładowy config.ini" -ForegroundColor Green
    Write-Host "⚠ WAŻNE: Edytuj $ConfigPath i wstaw prawdziwy refresh_token!" -ForegroundColor Yellow
    Write-Host ""
}

# Pytaj o credentials
$username = Read-Host "Podaj username dla BasicAuth"
$password = Read-Host "Podaj password dla BasicAuth"
$refreshToken = Read-Host "Podaj startowy refresh_token"

# Edycja appsettings.json
$appsettingsPath = Join-Path $publishPath "appsettings.json"
if (Test-Path $appsettingsPath) {
    $appsettings = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    $appsettings.Anaf.BasicAuth.Username = $username
    $appsettings.Anaf.BasicAuth.Password = $password
    $appsettings.Anaf.InitialRefreshToken = $refreshToken
    $appsettings.Anaf.ConfigFilePath = $ConfigPath
    $appsettings.Anaf.BackupDirectory = $BackupPath
    $appsettings | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath -Encoding UTF8
    Write-Host "✓ Zaktualizowano appsettings.json" -ForegroundColor Green
} else {
    Write-Warning "Nie znaleziono appsettings.json w $publishPath"
}

if ($installAsService -eq 'Y' -or $installAsService -eq 'y') {
    # Zatrzymanie i usunięcie istniejącego serwisu
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "Zatrzymywanie istniejącego serwisu..." -ForegroundColor Yellow
        
        if ($existingService.Status -eq 'Running') {
            Stop-Service -Name $ServiceName -Force
            Start-Sleep -Seconds 2
        }
        
        Write-Host "Usuwanie istniejącego serwisu..." -ForegroundColor Yellow
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        Write-Host "✓ Stary serwis usunięty" -ForegroundColor Green
        Write-Host ""
    }

    # Instalacja nowego serwisu
    Write-Host "Instalowanie serwisu..." -ForegroundColor Yellow
    $binaryFullPath = Join-Path $publishPath "AnafAutoToken.Worker.exe"

    if (-not (Test-Path $binaryFullPath)) {
        Write-Error "Nie znaleziono pliku wykonywalnego: $binaryFullPath"
        exit 1
    }

    New-Service -Name $ServiceName `
        -BinaryPathName $binaryFullPath `
        -DisplayName $DisplayName `
        -Description $Description `
        -StartupType Automatic

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Serwis zainstalowany" -ForegroundColor Green
    } else {
        Write-Error "Błąd podczas instalacji serwisu"
        exit 1
    }
    Write-Host ""

    # Konfiguracja recovery options (automatyczny restart po błędzie)
    Write-Host "Konfigurowanie opcji recovery..." -ForegroundColor Yellow
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    Write-Host "✓ Recovery options skonfigurowane (automatyczny restart)" -ForegroundColor Green
    Write-Host ""

    # Uruchomienie serwisu
    Write-Host "Uruchamianie serwisu..." -ForegroundColor Yellow
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 3

    $service = Get-Service -Name $ServiceName
    if ($service.Status -eq 'Running') {
        Write-Host "✓ Serwis uruchomiony pomyślnie" -ForegroundColor Green
    } else {
        Write-Warning "Serwis zainstalowany ale nie uruchomiony. Status: $($service.Status)"
        Write-Host "Sprawdź logi w: $LogPath" -ForegroundColor Yellow
    }
    Write-Host ""
}


# Podsumowanie
Write-Host "========================================" -ForegroundColor Cyan
if ($installAsService -eq 'Y' -or $installAsService -eq 'y') {
    Write-Host "Instalacja serwisu zakończona!" -ForegroundColor Cyan
} else {
    Write-Host "Deploy zakończony!" -ForegroundColor Cyan
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($installAsService -eq 'Y' -or $installAsService -eq 'y') {
    Write-Host "Nazwa serwisu: $ServiceName" -ForegroundColor White
    $color = if ($service.Status -eq 'Running') { 'Green' } else { 'Yellow' }
    Write-Host "Status: $($service.Status)" -ForegroundColor $color
    Write-Host "Typ uruchomienia: Automatic" -ForegroundColor White
    Write-Host ""
}

Write-Host "Lokalizacje:" -ForegroundColor White
Write-Host "  Aplikacja: $publishPath" -ForegroundColor Gray
Write-Host "  Config:    $ConfigPath" -ForegroundColor Gray
Write-Host "  Backupy:   $BackupPath" -ForegroundColor Gray
Write-Host "  Logi:      $LogPath" -ForegroundColor Gray
Write-Host ""

if ($installAsService -eq 'Y' -or $installAsService -eq 'y') {
    Write-Host "Przydatne komendy:" -ForegroundColor White
    Write-Host "  Sprawdź status:  Get-Service $ServiceName" -ForegroundColor Gray
    Write-Host "  Zatrzymaj:       Stop-Service $ServiceName" -ForegroundColor Gray
    Write-Host "  Uruchom:         Start-Service $ServiceName" -ForegroundColor Gray
    Write-Host "  Restart:         Restart-Service $ServiceName" -ForegroundColor Gray
    Write-Host "  Odinstaluj:      sc.exe delete $ServiceName" -ForegroundColor Gray
    Write-Host "  Zobacz logi:     Get-Content '$LogPath\anaf-token-refresh-.log' -Tail 50 -Wait" -ForegroundColor Gray
} else {
    Write-Host "Aby uruchomić aplikację ręcznie:" -ForegroundColor White
    Write-Host "  Uruchom:         & '$publishPath\AnafAutoToken.Worker.exe'" -ForegroundColor Gray
    Write-Host "  Zobacz logi:     Get-Content '$LogPath\anaf-token-refresh-.log' -Tail 50 -Wait" -ForegroundColor Gray
}
Write-Host ""
