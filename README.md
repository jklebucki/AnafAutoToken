# AnafAutoToken - Automatyczne Odświeżanie Tokenów ANAF

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/) 
[![C#](https://img.shields.io/badge/C%23-12.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/) 
[![Entity Framework](https://img.shields.io/badge/Entity%20Framework-8.0-green.svg)](https://docs.microsoft.com/en-us/ef/) 
[![SQLite](https://img.shields.io/badge/SQLite-3.0-blue.svg)](https://www.sqlite.org/) 
[![Serilog](https://img.shields.io/badge/Serilog-3.0-yellow.svg)](https://serilog.net/) 
[![Polly](https://img.shields.io/badge/Polly-8.0-orange.svg)](https://github.com/App-vNext/Polly)

## 📋 Opis

**AnafAutoToken** to wieloplatformowy serwis .NET 8.0, który automatycznie odświeża tokeny dostępu ANAF (Administrația Națională de Administrare Fiscală) przed ich wygaśnięciem. Aplikacja działa jako serwis Windows lub systemd na Linuxie.

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
├── AnafAutoToken.Core/         # Business logic, services, interfaces
├── AnafAutoToken.Infrastructure/   # EF Core, HTTP client, repositories
└── AnafAutoToken.Shared/       # Configuration models, extensions
```

### Technologie:
- **.NET 8.0** (LTS) - Worker Service
- **Entity Framework Core 8.0** - SQLite
- **Serilog** - Structured logging
- **Polly** - Resilience policies
- **System.IdentityModel.Tokens.Jwt** - JWT validation
- **Primary Constructors** (C# 12)

## 📦 Wymagania

### Windows:
- Windows 10/11 lub Windows Server 2016+
- .NET 8.0 Runtime ([pobierz tutaj](https://dotnet.microsoft.com/download/dotnet/8.0))
- Uprawnienia administratora (do instalacji serwisu)

### Linux:
- Ubuntu 20.04+, Debian 11+, RHEL 8+, lub inna dystrybucja z systemd
- .NET 8.0 Runtime
- Uprawnienia root (sudo)

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
- **Sprawdzanie .NET 8**: przed publikacją skrypt weryfikuje obecność runtime .NET 8.0 i przerwie wykonanie, jeśli brak.
- **Publikacja**: wykonuje `dotnet publish` projektu `src/AnafAutoToken.Worker` w konfiguracji Release do wskazanego folderu (self-contained, `win-x64`, single file).
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
- ✅ Zainstaluje .NET 8.0 Runtime (jeśli brak)
- ✅ Opublikuje aplikację
- ✅ Utworzy użytkownika systemowego `anaftoken`
- ✅ Skopiuje pliki do `/opt/anafautotoken`
- ✅ Utworzy plik systemd service
- ✅ Włączy autostart i uruchomi serwis

## ⚙️ Konfiguracja

### 1. Edycja `appsettings.json`

Plik znajduje się w katalogu instalacji:
- **Windows:** `bin\Release\net8.0\publish\appsettings.json`
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
| `RefreshToken` | TEXT(500) | Użyty refresh token (hashowany) |
| `NewAccessToken` | TEXT(2000) | Nowy access token |
| `ExpiresAt` | DATETIME | Data wygaśnięcia nowego tokenu |
| `Success` | BOOLEAN | Czy operacja się powiodła |
| `ErrorMessage` | TEXT | Komunikat błędu (jeśli failed) |
| `CreatedAt` | DATETIME | Timestamp operacji |

**Lokalizacja:**
- **Windows:** `bin\Release\net8.0\publish\tokens.db`
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
# Publikacja
dotnet publish AnafAutoToken.Worker/AnafAutoToken.Worker.csproj -c Release

# Uruchomienie
cd bin/Release/net8.0/publish
dotnet AnafAutoToken.Worker.dll
```

**Uwaga:** Upewnij się, że `appsettings.json`, `config.ini` i katalogi `backups/`, `logs/` istnieją w katalogu roboczym.

## 📤 Eksport tokenów do JSON

W solucji jest też małe narzędzie CLI `AnafAutoToken.Exporter`, które można zbudować lub opublikować jako osobny plik EXE. Plik EXE należy umieścić w tym samym katalogu co `appsettings.json` i `tokens.db`.

Przykład publikacji:

```powershell
dotnet publish src\AnafAutoToken.Exporter\AnafAutoToken.Exporter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Lub gotowym skryptem:

```powershell
.\scripts\publish-exporter-single-file.ps1
```

Z własnym katalogiem docelowym:

```powershell
.\scripts\publish-exporter-single-file.ps1 -OutputPath "C:\AnafAutoToken"
```

Skrypt publikuje do katalogu tymczasowego i do folderu docelowego kopiuje tylko finalny `AnafAutoToken.Exporter.exe`.

Dostępne opcje:

```powershell
AnafAutoToken.Exporter.exe -ect
AnafAutoToken.Exporter.exe -eat
AnafAutoToken.Exporter.exe -h
```

- `-ect` eksportuje aktualny `access_token` i `refresh_token` do timestampowanego pliku JSON
- `-eat` eksportuje wszystkie poprawnie zapisane pary tokenów z SQLite do timestampowanego pliku JSON
- `-h` wyświetla pomoc po angielsku

## 🔧 Troubleshooting

### Problem: Serwis nie uruchamia się

**Sprawdź:**
1. Czy .NET 8.0 Runtime jest zainstalowany: `dotnet --list-runtimes`
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
