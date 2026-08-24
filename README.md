# AnafAutoToken - Automatyczne Odświeżanie Tokenów ANAF

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/) 
[![C#](https://img.shields.io/badge/C%23-12.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/) 
[![Entity Framework](https://img.shields.io/badge/Entity%20Framework-10.0-green.svg)](https://docs.microsoft.com/en-us/ef/) 
[![SQLite](https://img.shields.io/badge/SQLite-3.0-blue.svg)](https://www.sqlite.org/) 
[![Serilog](https://img.shields.io/badge/Serilog-3.0-yellow.svg)](https://serilog.net/) 
[![Polly](https://img.shields.io/badge/Polly-8.0-orange.svg)](https://github.com/App-vNext/Polly)

## 📋 Opis

**AnafAutoToken** to wieloplatformowy serwis .NET 10.0, który automatycznie odświeża tokeny dostępu ANAF (Administrația Națională de Administrare Fiscală) przed ich wygaśnięciem. Aplikacja działa jako serwis Windows lub systemd na Linuxie.

### Główne funkcje:
- ✅ Automatyczne sprawdzanie ważności tokenu JWT
- ✅ Odświeżanie tokenu 3 dni przed wygaśnięciem (konfigurowalne)
- ✅ Aktualizacja pliku `config.ini` z nowym tokenem
- ✅ Automatyczne tworzenie backupów z timestampem
- ✅ Przechowywanie historii w bazie SQLite
- ✅ Zaplanowane wykonanie o określonej godzinie
- ✅ Retry policies z Polly (3 próby, exponential backoff)
- ✅ Circuit breaker dla API (5 błędów → 5 min przerwy)
- ✅ Structured logging z Serilog (pliki + konsola)
- ✅ Graceful shutdown z anulowaniem zadań

## 🏗️ Architektura

Projekt wykorzystuje **Clean Architecture** z podziałem na warstwy:

```
AnafAutoToken/
├── AnafAutoToken.Worker/       # Entry point, BackgroundService, DI
├── AnafAutoToken.Exporter/     # CLI exporter for JSON token dumps
├── AnafAutoToken.Manager/      # WinForms UI: token viewer + settings editor
├── AnafAutoToken.Core/         # Business logic, services, interfaces
├── AnafAutoToken.Infrastructure/   # EF Core, HTTP client, repositories
└── AnafAutoToken.Shared/       # Configuration models, extensions
```

### Technologie:
- **.NET 10.0** (LTS) - Worker Service
- **Entity Framework Core 10.0** - SQLite
- **Serilog** - Structured logging
- **Polly** - Resilience policies
- **System.IdentityModel.Tokens.Jwt** - JWT validation
- **Primary Constructors** (C# 12)

## 📦 Wymagania

### Windows:
- Windows 10/11 lub Windows Server 2016+
- **Na maszynie docelowej nie jest wymagany żaden runtime .NET** - paczki są publikowane jako self-contained single file
- SDK .NET 10 ([pobierz tutaj](https://dotnet.microsoft.com/download/dotnet/10.0)) tylko na maszynie, która buduje paczkę
- Uprawnienia administratora (do instalacji serwisu)

### Linux:
- Ubuntu 20.04+, Debian 11+, RHEL 8+, lub inna dystrybucja z systemd
- **Na maszynie docelowej nie jest wymagany żaden runtime .NET**
- SDK .NET 10 tylko na maszynie, która buduje paczkę
- Uprawnienia root (sudo)

## 📦 Publikacja (single file, bez .NET na hoście)

Publikacja jest **self-contained** i **single file**: runtime .NET 10 (dla workera także
ASP.NET Core) oraz natywne biblioteki SQLite są wkompilowane w plik wykonywalny. **Na maszynie
docelowej nie trzeba instalować niczego** - ani SDK, ani runtime'u .NET. SDK .NET 10 jest
potrzebne wyłącznie tam, gdzie uruchamiasz skrypt publikacji.

### Wszystko jednym poleceniem

```powershell
.\scripts\publish-all.ps1
```

Skrypt buduje solucję, uruchamia testy jednostkowe, a następnie publikuje wszystkie trzy
programy **do jednego katalogu** `publish\` - dokładnie tak, jak ma wyglądać katalog
instalacyjny serwisu. Parametry:

| Parametr | Domyślnie | Opis |
|----------|-----------|------|
| `-Configuration` | `Release` | Konfiguracja kompilacji |
| `-Runtime` | `win-x64` | RID, np. `linux-x64`, `linux-arm64` |
| `-OutputPath` | `publish` | Wspólny katalog na wszystkie programy |
| `-SkipTests` | - | Pomija testy przed publikacją |
| `-Clean` | - | Czyści katalog docelowy przed publikacją |

```powershell
.\scripts\publish-all.ps1 -OutputPath "C:\AnafAutoToken" -Clean
.\scripts\publish-all.ps1 -Runtime linux-x64 -OutputPath "C:\out\linux"
```

Dla runtime'ów innych niż `win-*` menedżer (WinForms) jest pomijany z ostrzeżeniem, a worker
i eksporter publikują się normalnie. Testy, które nie przejdą, przerywają publikację - chyba
że użyjesz `-SkipTests`.

### Pojedyncze programy

```powershell
.\scripts\publish-worker-single-file.ps1      # usługa (publish\AnafAutoToken.Worker)
.\scripts\publish-exporter-single-file.ps1    # CLI    (publish\AnafAutoToken.Exporter)
.\scripts\publish-manager-single-file.ps1     # UI     (publish\AnafAutoToken.Manager)
```

Przyjmują te same parametry `-Configuration`, `-Runtime`, `-OutputPath`:

```powershell
.\scripts\publish-worker-single-file.ps1 -OutputPath "C:\AnafAutoToken"
.\scripts\publish-worker-single-file.ps1 -Runtime linux-x64 -OutputPath "C:\out\linux"
```

### Co powstaje

Po `publish-all.ps1` katalog wygląda tak:

```
publish\
├── AnafAutoToken.Worker.exe        ~52 MB   usługa
├── AnafAutoToken.Exporter.exe      ~40 MB   CLI
├── AnafAutoToken.Manager.exe       ~51 MB   UI
├── appsettings.json                         konfiguracja (edytowalna)
├── register_service.bat
├── unregister_service.bat
└── EmailTemplates\                          szablony powiadomień
```

Wszystkie trzy programy leżą obok siebie celowo: eksporter i menedżer szukają
`appsettings.json` oraz `tokens.db` w swoim katalogu, a worker czyta `appsettings.json`
i `EmailTemplates\` z dysku w czasie działania. Reszta - łącznie z całym runtime'em -
siedzi wewnątrz plików EXE.

Pojedyncze skrypty publikują tylko swój program: `publish-worker-single-file.ps1` wnosi EXE
razem z plikami towarzyszącymi, a `publish-exporter-single-file.ps1` i
`publish-manager-single-file.ps1` kopiują wyłącznie swój plik EXE.

Instalacja z gotowej paczki, bez SDK na hoście:

```powershell
.\scripts\install-windows-service.ps1 -ArtifactPath "D:\paczki\AnafAutoToken.Worker"
```

```bash
sudo ./scripts/install-linux-service.sh --artifact /tmp/AnafAutoToken.Worker
```

## 🚀 Instalacja

### Windows (PowerShell jako Administrator)

```powershell
# 1. Sklonuj repozytorium
git clone https://github.com/your-repo/AnafAutoToken.git
cd AnafAutoToken

# 2. Uruchom skrypt instalacyjny (jako Administrator)
.\scripts\install-windows-service.ps1
```

Uwagi do skryptu `install-windows-service.ps1`:
- **Interaktywny**: skrypt poprosi o kilka wartości (np. ścieżka do `config.ini`, folder instalacji, decyzja czy zainstalować jako serwis).
- **Dwa tryby**: bez parametrów skrypt sam publikuje workera (wymaga SDK .NET 10 na tej maszynie); z `-ArtifactPath <ścieżka>` instaluje gotową paczkę zbudowaną gdzie indziej - wtedy host nie potrzebuje ani SDK, ani runtime .NET.
- **Publikacja**: deleguje do `scripts\publish-worker-single-file.ps1` (Release, `win-x64`, self-contained, single file, natywne biblioteki SQLite w środku).
- **Katalogi**: tworzy katalogi pomocnicze (`backups`, `logs`) w katalogu instalacyjnym jeśli nie istnieją.
- **Instalacja serwisu**: po publikacji (jeżeli wybierzesz instalację jako serwis) skrypt:
  - tworzy/usunie istniejący serwis jeśli trzeba,
  - tworzy nową usługę Windows (`New-Service`) z automatycznym startem,
  - konfiguruje politykę restartu (restart po błędach) oraz uruchamia serwis.

Po uruchomieniu skryptu zobaczysz podsumowanie z lokalizacją aplikacji, katalogiem backupów i logów oraz statusem serwisu.

Jeżeli nie chcesz używać PowerShell do instalacji lub chcesz zarejestrować serwis ręcznie, skrypt publikacyjny umieszcza pliki pomocnicze `.bat` bezpośrednio w folderze publikacji aplikacji (czyli w `<install-folder>`). Po uruchomieniu `install-windows-service.ps1` w katalogu wyjściowym publikacji powinny znajdować się:

- `<install-folder>\\register_service.bat` — rejestruje `AnafAutoToken.Worker.exe` jako usługę Windows. Użycie:

```bat
REM uruchom z katalogu publikacji (gdzie jest AnafAutoToken.Worker.exe)
register_service.bat
```

Ten skrypt ustawia `AnafAutoTokenWorker` jako nazwę usługi, tworzy ją przez `sc create`, dodaje opis i próbuje natychmiast uruchomić usługę.

- `<install-folder>\\unregister_service.bat` — zatrzymuje i usuwa zarejestrowaną usługę. Użycie:

```bat
REM uruchom z katalogu publikacji (lub jako Administrator)
unregister_service.bat
```

Uwaga: oba pliki `.bat` zakładają, że w tym samym katalogu znajduje się `AnafAutoToken.Worker.exe`. Jeśli publikujesz aplikację do innego folderu, skopiuj te pliki do folderu publikacji lub uruchom je z tego folderu.

Krótka ścieżka ręczna (jeśli nie używasz instalatora):

1. Wykonaj `dotnet publish src\\AnafAutoToken.Worker\\AnafAutoToken.Worker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o <install-folder>`
2. Skopiuj pliki do docelowego katalogu (`<install-folder>`)
3. Uruchom `register_service.bat` w tym katalogu, aby zarejestrować i uruchomić usługę

Jeśli potrzebujesz tylko uruchamiać aplikację ręcznie (bez instalowania jako serwis), możesz uruchomić plik EXE bezpośrednio:

```powershell
Start-Process -FilePath "<install-folder>\\AnafAutoToken.Worker.exe"
```

***

### Linux (Bash jako root/sudo)

```bash
# 1. Sklonuj repozytorium
git clone https://github.com/your-repo/AnafAutoToken.git
cd AnafAutoToken

# 2. Nadaj uprawnienia wykonywania
chmod +x install-linux-service.sh

# 3. Uruchom skrypt instalacyjny
sudo ./install-linux-service.sh
```

Skrypt automatycznie:
- ✅ Opublikuje aplikację jako self-contained single file (bez instalowania .NET na hoście)
- ✅ Utworzy użytkownika systemowego `anaftoken`
- ✅ Skopiuje pliki do `/opt/anafautotoken`
- ✅ Utworzy plik systemd service
- ✅ Włączy autostart i uruchomi serwis

## ⚙️ Konfiguracja

### 1. Edycja `appsettings.json`

Plik znajduje się w katalogu instalacji:
- **Windows:** `<katalog instalacji>\appsettings.json`
- **Linux:** `/opt/anafautotoken/appsettings.json`

```json
{
  "Anaf": {
    "TokenEndpoint": "https://logincert.anaf.ro/anaf-oauth2/v1/token",
    "BasicAuth": {
      "Username": "<ANAF_BASIC_AUTH_USERNAME>",      // ⚠️ WYMAGANE
      "Password": "<ANAF_BASIC_AUTH_PASSWORD>"        // ⚠️ WYMAGANE
    },
    "CheckSchedule": {
      "CheckHour": 16,                         // Godzina sprawdzenia (0-23)
      "CheckMinute": 13                        // Minuta sprawdzenia (0-59)
    },
    "DaysBeforeExpiration": 3,           // Odśwież N dni przed wygaśnięciem
    "ConfigFilePath": "c:\\tmp\\config.ini",      // Ścieżka do config.ini
    "BackupDirectory": "c:\\tmp\\backups",        // Katalog backupów
    "InitialRefreshToken": "<INITIAL_REFRESH_TOKEN>",            // Opcjonalny token początkowy
    "Email": {
      "SmtpServer": "<SMTP_SERVER>",
      "SmtpPort": 465,
      "Username": "<SMTP_USERNAME>",
      "Password": "<SMTP_PASSWORD>",
      "FromAddress": "<FROM_ADDRESS>",
      "FromName": "ANAF Auto Token Service",
      "ToAddresses": ["admin@example.com"],
      "EnableSsl": true
    }
  },
  "ConnectionStrings": {
    "TokenDatabase": "Data Source=tokens.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

**⚠️ WAŻNE:** Ustaw poprawne wartości `Username` i `Password` dla ANAF API!

### 1.1 Sekretne dane lokalne (`appsettings.secrets.json`)

W repozytorium `appsettings.json` zawiera tylko placeholdery dla **BasicAuth**, **Email** i **InitialRefreshToken**. Utwórz plik `appsettings.secrets.json` obok `appsettings.json` z rzeczywistymi poświadczeniami.

**WAŻNE:** Podczas budowania (`dotnet build`) lub publikowania (`dotnet publish`) mechanizm MSBuild automatycznie **scala** zawartość `appsettings.secrets.json` z `appsettings.json` w folderze wyjściowym, zastępując placeholdery rzeczywistymi wartościami. Plik `appsettings.secrets.json` jest ignorowany przez Git, dzięki czemu poświadczenia nigdy nie trafiają do repozytorium.

Przykład zawartości `appsettings.secrets.json`:

```json
{
  "Anaf": {
    "BasicAuth": {
      "Username": "real-username",
      "Password": "real-password"
    },
    "Email": {
      "SmtpServer": "smtp.example.com",
      "SmtpPort": 587,
      "Username": "real-email@example.com",
      "Password": "real-email-password",
      "FromAddress": "anaf-token-service@example.com",
      "FromName": "ANAF Auto Token Service",
      "ToAddresses": ["admin@example.com"],
      "EnableSsl": true
    },
    "InitialRefreshToken": "real-refresh-token"
  }
}
```

**Mechanizm działania:**
1. W repozytorium commitowany jest tylko `appsettings.json` z placeholderami
2. Każdy developer tworzy lokalnie `appsettings.secrets.json` z rzeczywistymi danymi
3. Podczas `dotnet build` lub `dotnet publish` skrypt PowerShell (`scripts/merge-secrets.ps1`) automatycznie scala oba pliki
4. Wynikowy `appsettings.json` w `bin/` lub `publish/` zawiera rzeczywiste poświadczenia
5. Plik źródłowy `appsettings.secrets.json` nigdy nie jest kopiowany do wyjścia - tylko jego wartości

### 2. Plik `config.ini`

Plik `config.ini` jest elementem aplikacji pośredniczącej w wymianie informacji ANAF. Lokalizacja tego pliku to najcześciej `C:\Program Files\Apache Software Foundation\Tomcat 10.1\webapps\Anaf`

W pliku `config.ini` pod wsakzaną sekcją system uzupełnia pobrany token. **Uwaga!!!** Wcześniejszy token musi być uzupełniony bo równocześnie dostarcza informację o dacie wygaśniecia.
```ini
[AcessToken]
```

### 3. Pierwszy token refresh
Musisz podać początkowy `refresh_token` w `appsettings.json` 
```json
"InitialRefreshToken": "your_initial_refresh_token"
```

## 🎯 Działanie

### Harmonogram sprawdzeń:

1. **Sprawdzenie co godzinę** - aplikacja budzi się co godzinę i sprawdza czy jest zaplanowana godzina
2. **Wykonanie o określonej godzinie** - np. codziennie o 02:00 (wg `CheckSchedule`)
3. **Weryfikacja tokenu JWT** - parsowanie i sprawdzenie daty wygaśnięcia
4. **Warunek odświeżenia:**
   ```
   Dni do wygaśnięcia ≤ DaysBeforeExpiration (domyślnie 3)
   ```
5. **Wywołanie ANAF API** - POST z `refresh_token` + Basic Auth
6. **Backup config.ini** → `bak_config_ini_YYYYMMDD_HHmmss.txt`
7. **Aktualizacja config.ini** z nowym tokenem
8. **Zapis do bazy SQLite** - historia odświeżeń

### Polityki resilience (Polly):

**Retry Policy:**
- 3 próby z exponential backoff: 2s, 4s, 8s
- Logowanie każdej próby

**Circuit Breaker:**
- Otwiera się po 5 kolejnych błędach
- Przerwa: 5 minut
- Logowanie zdarzeń otwarcia/zamknięcia

## 📊 Baza danych (SQLite)

Tabela: `TokenRefreshLogs`

| Kolumna | Typ | Opis |
|---------|-----|------|
| `Id` | INTEGER | Primary key |
| `RefreshToken` | TEXT | Refresh token **po** odświeżeniu (przy błędzie: token, którym próbowano) |
| `AccessToken` | TEXT | Nowy access token (pusty przy błędzie) |
| `ExpiresAt` | DATETIME | Data wygaśnięcia nowego access tokenu |
| `RefreshTokenExpiresAt` | DATETIME | Data wygaśnięcia refresh tokenu (może być NULL dla starych wpisów) |
| `IsSuccess` | BOOLEAN | Czy operacja się powiodła |
| `ErrorMessage` | TEXT | Komunikat błędu (jeśli failed) |
| `ResponseStatusCode` | INTEGER | Kod HTTP odpowiedzi ANAF |
| `CreatedAt` | DATETIME | Timestamp operacji (UTC) |

### Rotacja refresh tokenu

ANAF zwraca nowy `refresh_token` przy każdym odświeżeniu. Obowiązują następujące zasady:

1. Do wywołania `/token` używany jest **najnowszy udany wpis** z `TokenRefreshLogs`
   (sortowanie `CreatedAt DESC, Id DESC`); dopiero gdy tabela jest pusta, brany jest
   `Anaf:InitialRefreshToken` z konfiguracji.
2. Nowy wpis jest zapisywany do bazy **przed** aktualizacją `config.ini`. Refresh token jest
   jedyną wartością, której nie da się odzyskać z żadnego innego miejsca, a nieaktualny access
   token w `config.ini` naprawi się przy kolejnym przebiegu.
3. Jeśli ANAF nie zwróci pola `refresh_token`, zapisywany jest dotychczasowy token
   (z ostrzeżeniem w logu) zamiast odrzucania całego odświeżenia.

**Lokalizacja:**
- **Windows:** `<katalog instalacji>\tokens.db`
- **Linux:** `/opt/anafautotoken/tokens.db`

## 📝 Logi

### Serilog - dwa sinki:

**1. File Sink** (rolling daily, 30 dni retencji):
- **Windows:** `logs\anaf-token-refresh-YYYYMMDD.log`
- **Linux:** `/opt/anafautotoken/logs/anaf-token-refresh-YYYYMMDD.log`

**2. Console Sink** (output format: `[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}`)

### Przykładowe logi:

```
[14:23:15 INF] ANAF Token Refresh Worker starting at: 04/15/2025 14:23:15
[14:23:15 INF] Database migrated successfully
[02:00:01 INF] Executing scheduled token check...
[02:00:01 INF] Current token parsed successfully. Expires at: 2025-04-22 12:00:00
[02:00:01 INF] Token is expiring in 2 days. Refreshing...
[02:00:02 INF] Polly Retry: Attempt 1 for POST https://api.anaf.ro/prod/FCTEL/rest/token
[02:00:03 INF] Token refreshed successfully from ANAF API
[02:00:03 INF] Config backup created: backups\bak_config_ini_20250415_020003.txt
[02:00:03 INF] Config file updated with new token
[02:00:03 INF] Token refresh logged to database (ID: 42)
```

## 🛠️ Zarządzanie serwisem

### Windows (PowerShell jako Administrator)

```powershell
# Status serwisu
Get-Service AnafAutoToken

# Zatrzymaj
Stop-Service AnafAutoToken

# Uruchom
Start-Service AnafAutoToken

# Restart
Restart-Service AnafAutoToken

# Zobacz logi (real-time)
Get-Content "logs\anaf-token-refresh-*.log" -Tail 50 -Wait

# Odinstaluj
sc.exe delete AnafAutoToken
```

### Linux (Bash)

```bash
# Status serwisu
systemctl status anaf-auto-token

# Zatrzymaj
sudo systemctl stop anaf-auto-token

# Uruchom
sudo systemctl start anaf-auto-token

# Restart
sudo systemctl restart anaf-auto-token

# Zobacz logi (real-time)
journalctl -u anaf-auto-token -f

# Logi aplikacji (Serilog)
tail -f /opt/anafautotoken/logs/anaf-token-refresh-*.log

# Wyłącz autostart
sudo systemctl disable anaf-auto-token

# Odinstaluj
sudo systemctl stop anaf-auto-token
sudo systemctl disable anaf-auto-token
sudo rm /etc/systemd/system/anaf-auto-token.service
sudo systemctl daemon-reload
```

## 🧪 Testowanie lokalne (bez instalacji serwisu)

```bash
dotnet run --project src/AnafAutoToken.Worker
```

Albo z gotowej paczki single file:

```powershell
.\scripts\publish-worker-single-file.ps1 -OutputPath C:	mpnaf
C:	mpnaf\AnafAutoToken.Worker.exe
```

**Uwaga:** Upewnij się, że `appsettings.json`, `config.ini` i katalogi `backups/`, `logs/` istnieją w katalogu roboczym.

## 📤 Eksport tokenów do JSON

W solucji jest też małe narzędzie CLI `AnafAutoToken.Exporter`. Plik EXE należy umieścić w tym samym katalogu co `appsettings.json` i `tokens.db`.

Publikacja (szczegóły w sekcji [Publikacja](#-publikacja-single-file-bez-net-na-hoście)):

```powershell
.\scripts\publish-exporter-single-file.ps1 -OutputPath "C:\AnafAutoToken"
```

Skrypt publikuje do katalogu tymczasowego i do folderu docelowego kopiuje tylko finalny, samowystarczalny `AnafAutoToken.Exporter.exe`.

Dostępne opcje:

```powershell
AnafAutoToken.Exporter.exe -ect
AnafAutoToken.Exporter.exe -eat
AnafAutoToken.Exporter.exe -h
```

- `-ect` eksportuje aktualny `access_token` i `refresh_token` do timestampowanego pliku JSON
- `-eat` eksportuje wszystkie poprawnie zapisane pary tokenów z SQLite do timestampowanego pliku JSON
- `-h` wyświetla pomoc po angielsku

## 🌐 API workera

Worker wystawia minimalne API pod adresem z `Api:Url` (domyślnie `http://127.0.0.1:5099`):

| Metoda | Ścieżka | Opis |
|--------|---------|------|
| `GET` | `/api/tokens/current` | Zwraca aktualny access i refresh token wraz z datami wygaśnięcia |
| `POST` | `/api/tokens/refresh` | Wymusza natychmiastowe sprawdzenie i odświeżenie tokenu |

`POST /api/tokens/refresh` zwraca `200` z wynikiem operacji albo `409`, jeśli odświeżanie
już trwa. Przykład:

```powershell
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5099/api/tokens/refresh"
```

```json
{
  "IsSuccess": true,
  "TokenWasRefreshed": false,
  "NewExpirationDate": "2027-03-12T08:31:28Z",
  "ErrorMessage": null,
  "StartedAtUtc": "2026-08-24T08:31:28.4195118Z",
  "CompletedAtUtc": "2026-08-24T08:31:28.8638766Z"
}
```

> ⚠️ Oba endpointy są nieuwierzytelnione, a `GET` zwraca tokeny w postaci jawnej.
> Domyślne `Api:Url` wiąże nasłuch do pętli zwrotnej (`127.0.0.1`) i tak powinno zostać.
> Nie ustawiaj `0.0.0.0` ani adresu zewnętrznego bez postawienia przed workerem
> uwierzytelnionego proxy.

## 🖥️ Menedżer (UI) - `AnafAutoToken.Manager`

Okienkowa (WinForms, Windows-only) nakładka na to samo `appsettings.json` i `tokens.db`,
którymi posługuje się serwis. Uruchamiana niezależnie od serwisu - nie trzeba go zatrzymywać,
żeby zajrzeć do bazy.

Publikacja jako pojedynczy, samowystarczalny plik EXE:

```powershell
.\scripts\publish-manager-single-file.ps1 -OutputPath "C:\AnafAutoToken"
```

Po starcie program szuka `appsettings.json` obok siebie, a ścieżkę do bazy bierze z
`ConnectionStrings:TokenDatabase`. Obie ścieżki można też wskazać ręcznie.

**Zakładka „Baza danych”**
- pełna historia `TokenRefreshLogs` (najnowsze na górze) wraz z datami wygaśnięcia obu tokenów
- podsumowanie, z którego wpisu pochodzi refresh token używany przy następnym odświeżeniu
- pełna treść access i refresh tokenu zaznaczonego wpisu
- kopiowanie do schowka: sam token, zaznaczony wpis jako JSON, cała historia jako JSON
- zapis całej historii do pliku JSON

**Zakładka „Serwis systemowy”** (tylko Windows)
- stan usługi odświeżany **co 5 sekund**: `DZIAŁA` / `ZATRZYMANY` / `NIE ZAREJESTROWANY`
  (plus stany przejściowe), z nazwą wyświetlaną, typem startu, ścieżką z rejestru
  (`HKLM\SYSTEM\CurrentControlSet\Services\<nazwa>\ImagePath`) i godziną ostatniego odczytu
- **Zarejestruj** - zakłada usługę (`sc create`, start automatyczny) wraz z opisem i polityką
  restartu 3 × co 60 s, dokładnie taką samą jak w `install-windows-service.ps1`
- **Wyrejestruj** - zatrzymuje usługę, jeśli działa, i ją usuwa (`sc delete`)
- **Uruchom / Zatrzymaj / Restartuj** - przez `ServiceController`, z czekaniem do 30 s na
  osiągnięcie docelowego stanu; operacje idą w tle, więc okno nie zamarza
- przyciski włączają się zależnie od stanu (np. „Uruchom” tylko dla zatrzymanej usługi)
- nazwa serwisu, nazwa wyświetlana, opis i ścieżka do `AnafAutoToken.Worker.exe` są
  edytowalne; ścieżka domyślnie wskazuje katalog wczytanego `appsettings.json`
- rejestracja, start i stop wymagają uprawnień administratora - jeśli ich brak, u góry
  zakładki pojawia się ostrzeżenie i przycisk **Uruchom ponownie jako Administrator**

**Ręczne odświeżenie tokenu** (grupa na zakładce „Serwis systemowy”)
- przycisk **Odśwież token teraz** wywołuje `POST /api/tokens/refresh` na działającym
  workerze - celowo, a nie w procesie menedżera: dwa procesy pisałyby jednocześnie do
  `config.ini` i do bazy, a ANAF dostałby dwa żądania z tym samym refresh tokenem
- worker wykonuje pełną procedurę: sprawdza ważność access tokenu, w razie potrzeby odpytuje
  ANAF, zapisuje rotowany refresh token, aktualizuje `config.ini` i wysyła powiadomienia
- wynik ląduje pod przyciskiem: `Token odświeżony` (zielony), `Odświeżenie nie było potrzebne`
  (bursztynowy) albo treść błędu (czerwony), zawsze ze znacznikiem czasu i czasem trwania
- po udanym odświeżeniu podgląd bazy przeładowuje się sam, więc nowy wpis jest od razu widoczny
- adres brany jest z `Api:Url` na zakładce „Konfiguracja”, a nasłuch `0.0.0.0` jest
  tłumaczony na `127.0.0.1`; jeśli worker nie odpowiada, komunikat kieruje do zakładki serwisu
- równoległe wywołania są odrzucane (HTTP 409) - ręczne odświeżenie i zaplanowany przebieg
  o `CheckHour` nie mogą się na siebie nałożyć

**Zakładka „Konfiguracja”**
- wszystkie parametry: endpoint ANAF, Basic Auth, harmonogram, `DaysBeforeExpiration`,
  ścieżki `config.ini` i katalogu backupów, `InitialRefreshToken`, pełne ustawienia SMTP
  z listą odbiorców, connection string bazy, adres API workera i poziomy logowania
- hasła są maskowane; checkbox „Pokaż hasła i sekrety” je odsłania

**Zakładka „JSON (surowy)”**
- podgląd i edycja całego pliku, także kluczy spoza formularza
- „Zastosuj JSON do formularza” waliduje treść przed przeniesieniem jej do dokumentu

Zapis (przycisk **Zapisz** na górze okna) tworzy kopię `appsettings.bak_RRRRMMDD_GGMMSS.json`
obok pliku i **zachowuje klucze, których nie ma w formularzu**.

> ⚠️ Serwis czyta ustawienia przy starcie - po zapisie zrestartuj usługę
> (najprościej przyciskiem **Restartuj** na zakładce „Serwis systemowy”).
> Jeśli serwis jest zainstalowany w `Program Files`, uruchom menedżera jako Administrator.
> W repozytorium `appsettings.json` w katalogu `src\AnafAutoToken.Worker` jest nadpisywany
> przy każdym buildzie przez `appsettings.secrets.json` - menedżer służy do edycji pliku
> **w katalogu instalacyjnym**, nie w źródłach.

## 🔧 Troubleshooting

### Problem: Serwis nie uruchamia się

**Sprawdź:**
1. Czy plik EXE jest kompletny - paczka jest self-contained, więc runtime nie musi być zainstalowany na hoście
2. Uprawnienia do plików (Linux): `chown -R anaftoken:anaftoken /opt/anafautotoken`
3. Logi startowe:
   - **Windows:** Event Viewer → Windows Logs → Application
   - **Linux:** `journalctl -u anaf-auto-token -n 100`

### Problem: Token nie jest odświeżany

**Sprawdź:**
1. Czy godzina sprawdzenia jest poprawna w `appsettings.json` (`CheckSchedule`)
2. Czy `config.ini` zawiera poprawny `refresh_token`
3. Czy credentials w `BasicAuth` są poprawne
4. Logi aplikacji w katalogu `logs/`

### Problem: Błąd 401 Unauthorized z ANAF API

**Przyczyna:** Niepoprawne credentials w `BasicAuth`

**Rozwiązanie:**
1. Sprawdź `Username` i `Password` w `appsettings.json`
2. Zweryfikuj z dokumentacją ANAF
3. Restart serwisu po zmianie konfiguracji

### Problem: Database locked (SQLite)

**Przyczyna:** Wiele procesów próbuje pisać do bazy

**Rozwiązanie:**
1. Upewnij się, że tylko jedna instancja serwisu działa
2. Sprawdź czy baza nie jest otwarta w innej aplikacji (DB Browser)

## 📚 Struktura backupów

Format backupu: `bak_config_ini_YYYYMMDD_HHmmss.txt`

Przykład:
```
backups/
├── bak_config_ini_20250415_020003.txt
├── bak_config_ini_20250418_020001.txt
└── bak_config_ini_20250421_020005.txt
```

**Zalecenie:** Regularnie archiwizuj/usuwaj stare backupy.

## 🔐 Bezpieczeństwo

### Zalecenia:

1. **Ochrona credentials:**
   - Ustaw uprawnienia do `appsettings.json`:
     - **Windows:** Tylko Administrator i SYSTEM
     - **Linux:** `chmod 600 /opt/anafautotoken/appsettings.json`

2. **Refresh token:**
   - Przechowuj bezpiecznie poza repozytorium
   - Użyj Azure Key Vault / AWS Secrets Manager w produkcji

3. **Logi:**
   - Nie commituj plików `.log` do Git
   - Regularnie rotuj (Serilog robi to automatycznie - 30 dni)

4. **SQLite:**
   - Backup bazy regularnie
   - Rozważ encryption at rest w produkcji

## 📄 Licencja

MIT License - możesz swobodnie używać, modyfikować i dystrybuować.

## 🤝 Wsparcie

W razie problemów:
1. Sprawdź sekcję **Troubleshooting** powyżej
2. Przejrzyj logi aplikacji
3. Otwórz issue na GitHub z pełnymi logami

## 📝 Changelog

### v1.0.0 (2025-04-15)
- ✨ Pierwsza wersja
- ✅ Obsługa Windows/Linux services
- ✅ Automatyczne odświeżanie tokenów
- ✅ SQLite historia
- ✅ Polly retry policies
- ✅ Serilog logging
- ✅ Instalatory PowerShell/Bash

---

**Autor:** AnafAutoToken Team  
**Kontakt:** j.klebucki@ajksoftware.pl  
**Repository:** https://github.com/jklebucki/AnafAutoToken
