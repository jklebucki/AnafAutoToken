<#
.SYNOPSIS
    Buduje i publikuje wszystkie elementy AnafAutoToken jednym poleceniem.

.DESCRIPTION
    Uruchamia testy, a następnie publikuje workera i menedżera jako samowystarczalne
    pliki single file (runtime .NET 10 w środku). Na maszynie docelowej nie trzeba
    instalować ani SDK, ani runtime'u .NET.

    Wszystko ląduje w jednym katalogu - dokładnie tak, jak ma wyglądać katalog
    instalacyjny serwisu.

    Menedżer jest aplikacją WinForms, więc powstaje tylko dla runtime'ów win-*.
    Dla linux-x64 / linux-arm64 zostanie pominięty z ostrzeżeniem.

.PARAMETER OutputPath
    Jeden wspólny katalog na oba programy. Trafiają do niego obok siebie:
    AnafAutoToken.Worker.exe, AnafAutoToken.Manager.exe oraz pliki towarzyszące
    workera (wzorzec appsettings.json, EmailTemplates\, *.bat).

    Konfiguracja robocza i baza danych NIE leżą w tym katalogu - mieszkają
    w C:\ProgramData\AnafAutoToken i przeżywają wdrożenie nowej wersji.

.PARAMETER SkipTests
    Pomija uruchomienie testów jednostkowych przed publikacją.

.PARAMETER Clean
    Czyści katalog docelowy przed publikacją.

.EXAMPLE
    .\scripts\publish-all.ps1

.EXAMPLE
    .\scripts\publish-all.ps1 -OutputPath "C:\AnafAutoToken" -Clean

.EXAMPLE
    .\scripts\publish-all.ps1 -Runtime linux-x64 -OutputPath "C:\out\linux"
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "publish",
    [switch]$SkipTests,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

[Console]::InputEncoding  = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "AnafAutoToken.sln"
$testProjectPath = Join-Path $repoRoot "tests\AnafAutoToken.Tests\AnafAutoToken.Tests.csproj"
$publishSingleFile = Join-Path $PSScriptRoot "publish-single-file.ps1"

$rootOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
$isWindowsRuntime = $Runtime -like "win-*"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "AnafAutoToken - publikacja wszystkich elementów" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Konfiguracja : $Configuration"
Write-Host "Runtime      : $Runtime"
Write-Host "Output       : $rootOutputPath"
Write-Host "Testy        : $(if ($SkipTests) { 'pominięte' } else { 'włączone' })"
Write-Host ""

# =======================================================
# WYMAGANIA MASZYNY BUDUJĄCEJ
# =======================================================
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "Nie znaleziono polecenia 'dotnet'. Do zbudowania paczek potrzebne jest SDK .NET 10 na TEJ maszynie."
    exit 1
}

if (-not ((dotnet --list-sdks) | Where-Object { $_ -match '^10\.' })) {
    Write-Error "Nie znaleziono SDK .NET 10. Pobierz z https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
}

# =======================================================
# CZYSZCZENIE
# =======================================================
if ($Clean -and (Test-Path $rootOutputPath)) {
    Write-Host "Czyszczenie katalogu $rootOutputPath..." -ForegroundColor Yellow
    Remove-Item -Path $rootOutputPath -Recurse -Force
    Write-Host ""
}

New-Item -ItemType Directory -Path $rootOutputPath -Force | Out-Null

# =======================================================
# BUDOWANIE CAŁEJ SOLUCJI (szybki wyłap błędów kompilacji)
# =======================================================
Write-Host "[1/3] Budowanie solucji..." -ForegroundColor Yellow
dotnet build $solutionPath -c $Configuration --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd kompilacji solucji."
    exit 1
}

Write-Host "Solucja zbudowana." -ForegroundColor Green
Write-Host ""

# =======================================================
# TESTY
# =======================================================
if ($SkipTests) {
    Write-Host "[2/3] Testy pominięte (-SkipTests)." -ForegroundColor Yellow
    Write-Host ""
}
else {
    Write-Host "[2/3] Uruchamianie testów..." -ForegroundColor Yellow
    dotnet test $testProjectPath -c $Configuration --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Testy nie przeszły - publikacja przerwana. Użyj -SkipTests, aby ją wymusić."
        exit 1
    }

    Write-Host "Testy zielone." -ForegroundColor Green
    Write-Host ""
}

# =======================================================
# PUBLIKACJA
# =======================================================
Write-Host "[3/3] Publikacja pakietów single file..." -ForegroundColor Yellow
Write-Host ""

$targets = @(
    @{ Project = "Worker";  AssemblyName = "AnafAutoToken.Worker";  WindowsOnly = $false },
    @{ Project = "Manager"; AssemblyName = "AnafAutoToken.Manager"; WindowsOnly = $true }
)

$results = @()

foreach ($target in $targets) {
    if ($target.WindowsOnly -and -not $isWindowsRuntime) {
        Write-Warning "Pomijam $($target.AssemblyName) - to aplikacja WinForms, a runtime to $Runtime."
        $results += [pscustomobject]@{
            Program = $target.AssemblyName
            Status  = "pominięty ($Runtime)"
            Rozmiar = "-"
            Plik    = "-"
        }
        continue
    }

    # Wszystkie trzy programy do tego samego katalogu. Worker idzie pierwszy, bo tylko on
    # wnosi pliki towarzyszące; pozostałe kopiują wyłącznie swój plik wykonywalny, więc
    # nie ma szans na nadpisanie appsettings.json ani katalogu EmailTemplates.
    & $publishSingleFile `
        -Project $target.Project `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -OutputPath $rootOutputPath

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Błąd publikacji projektu $($target.AssemblyName)."
        exit 1
    }

    $executableName = if ($isWindowsRuntime) { "$($target.AssemblyName).exe" } else { $target.AssemblyName }
    $executablePath = Join-Path $rootOutputPath $executableName

    $results += [pscustomobject]@{
        Program = $target.AssemblyName
        Status  = "OK"
        Rozmiar = "$([math]::Round((Get-Item $executablePath).Length / 1MB, 1)) MB"
        Plik    = $executableName
    }
}

# =======================================================
# PODSUMOWANIE
# =======================================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ZAKOŃCZONO" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Katalog: $rootOutputPath" -ForegroundColor Cyan
$results | Format-Table -AutoSize

Write-Host "Zawartość katalogu:"
Get-ChildItem -Path $rootOutputPath | Sort-Object PSIsContainer, Name | ForEach-Object {
    Write-Host "  $($_.Name)$(if ($_.PSIsContainer) { [char]92 })"
}

Write-Host ""
Write-Host "Runtime .NET 10 jest wbudowany w każdy plik wykonywalny." -ForegroundColor Green
Write-Host "Maszyna docelowa nie wymaga instalacji SDK ani runtime'u .NET." -ForegroundColor Green
Write-Host "Konfiguracja i baza powstaną w C:\ProgramData\AnafAutoToken przy pierwszym uruchomieniu." -ForegroundColor Green
Write-Host ""
Write-Host "Instalacja usługi z gotowej paczki (bez SDK na hoście):"

if ($isWindowsRuntime) {
    Write-Host "  .\scripts\install-windows-service.ps1 -ArtifactPath `"$rootOutputPath`""
}
else {
    Write-Host "  sudo ./scripts/install-linux-service.sh --artifact <ścieżka do skopiowanej paczki>"
}

Write-Host ""
