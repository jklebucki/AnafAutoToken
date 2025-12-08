# AnafAutoToken - Testy Jednostkowe

## Przegląd

Projekt testów jednostkowych dla aplikacji AnafAutoToken zawiera kompleksowe testy dla kluczowych komponentów aplikacji, skupiając się na logice biznesowej i zachowaniach wymagających weryfikacji.

## Pokrycie Testami

### Przetestowane Komponenty

#### 1. **JwtExtensions** (Extensions/JwtExtensionsTests.cs)

Testy dla rozszerzeń JWT obejmujących:

* ✅ `GetExpirationDate()` - parsowanie daty wygaśnięcia tokena
* ✅ `IsExpiringSoon()` - sprawdzanie czy token wygasa w określonym czasie
* ✅ `IsValid()` - walidacja ważności tokena

**Scenariusze testowe:**

* Tokeny z poprawną datą wygaśnięcia
* Tokeny nieprawidłowe/uszkodzone
* Tokeny puste i null
* Tokeny wygasłe
* Tokeny wygasające w różnych okresach względem progu
* Tokeny z białymi znakami

#### 2. **TokenValidationService** (Services/TokenValidationServiceTests.cs)

Testy dla serwisu walidacji tokenów:

* ✅ `ShouldRefreshToken()` - decyzja o odświeżeniu tokena
* ✅ `GetExpirationDate()` - pobieranie daty wygaśnięcia
* ✅ `IsTokenValid()` - sprawdzanie ważności tokena

**Scenariusze testowe:**

* Tokeny wymagające odświeżenia (przed progiem)
* Tokeny nie wymagające odświeżenia (po progu)
* Tokeny wygasłe
* Tokeny nieprawidłowe
* Weryfikacja logowania błędów

#### 3. **ConfigFileService** (Services/ConfigFileServiceTests.cs)

Testy dla operacji na pliku konfiguracyjnym:

* ✅ `ReadAccessTokenAsync()` - odczyt tokena z pliku
* ✅ `UpdateAccessTokenAsync()` - aktualizacja tokena w pliku
* ✅ `CreateBackupAsync()` - tworzenie kopii zapasowej

**Scenariusze testowe:**

* Odczyt poprawnego tokena z pliku INI
* Obsługa brakującego pliku konfiguracyjnego
* Obsługa brakującego tokena w pliku
* Odczyt tokena z białymi znakami
* Aktualizacja tokena z zachowaniem struktury pliku
* Tworzenie kopii zapasowych z timestampem
* Automatyczne tworzenie katalogu backupów
* Wielokrotne tworzenie kopii zapasowych

#### 4. **EmailNotificationService** (Services/EmailNotificationServiceTests.cs)

Testy dla serwisu powiadomień email:

* ✅ Logika wykrywania poprawnej konfiguracji email
* ✅ Obsługa braku konfiguracji
* ✅ Walidacja wymaganych pól konfiguracji
* ✅ Obsługa brakujących szablonów

**Scenariusze testowe:**

* Sprawdzanie czy email jest skonfigurowany
* Pomijanie wysyłki przy braku konfiguracji
* Walidacja wymaganych pól (SmtpServer, FromAddress, ToAddresses)
* Obsługa błędów przy brakujących szablonach

## Uruchamianie Testów

### Wszystkie testy

```powershell
dotnet test tests/AnafAutoToken.Tests/AnafAutoToken.Tests.csproj
```

### Z szczegółowym outputem

```powershell
dotnet test tests/AnafAutoToken.Tests/AnafAutoToken.Tests.csproj --verbosity normal
```

### Z pomiarem pokrycia kodu

```powershell
dotnet test tests/AnafAutoToken.Tests/AnafAutoToken.Tests.csproj --collect:"XPlat Code Coverage"
```

## Struktura Projektu Testów

```
tests/AnafAutoToken.Tests/
├── Extensions/
│   └── JwtExtensionsTests.cs          # Testy rozszerzeń JWT
├── Services/
│   ├── ConfigFileServiceTests.cs      # Testy operacji na plikach
│   ├── EmailNotificationServiceTests.cs # Testy powiadomień email
│   └── TokenValidationServiceTests.cs  # Testy walidacji tokenów
└── AnafAutoToken.Tests.csproj
```

## Użyte Biblioteki

* **xUnit** - framework testowy
* **Moq** - mockowanie zależności
* **FluentAssertions** - czytelne asercje
* **Microsoft.IdentityModel.Tokens** - tworzenie tokenów JWT do testów
* **coverlet.collector** - pomiar pokrycia kodu

## Filozofia Testów

### Co testujemy:




✅ **Logika biznesowa** - parsowanie JWT, walidacja dat, operacje na plikach✅ **Obsługa błędów** - nieprawidłowe dane wejściowe, brakujące pliki✅ **Edge cases** - tokeny wygasłe, puste wartości, tokeny na granicy progu✅ **Zachowania krytyczne** - regex parsing, tworzenie backupów, walidacja konfiguracji

### Czego NIE testujemy:





❌ Prostych getterów/setterów❌ Frameworkowych mechanizmów (DI, logging infrastructure)❌ Zewnętrznych API (ANAF API - wymaga mocków)❌ Operacji wysyłki email (wymaga realnego SMTP)❌ Operacji bazodanowych (wymaga testowej bazy)

## Statystyki

* **Liczba testów:** 44
* **Sukces:** 100%
* **Klasy testowe:** 4
* **Metody testowe:** 44

## Notatki Implementacyjne

### Tworzenie tokenów JWT w testach

Tokeny JWT są tworzone przy użyciu `JwtSecurityTokenHandler` z dynamicznie ustawianą datą `NotBefore` (2 godziny przed `Expires`) aby umożliwić testowanie tokenów wygasłych.

### Testy ConfigFileService

Używają tymczasowych katalogów (`Path.GetTempPath()`) do izolacji testów i automatycznego czyszczenia po wykonaniu testów poprzez implementację `IDisposable`.

### Mockowanie loggerów

Mockowane loggery weryfikują tylko przypadki, gdzie logowanie błędów jest częścią kontraktu serwisu (np. przy faktycznych wyjątkach).

## Przyszłe Rozszerzenia

Potencjalne obszary do rozbudowy testów:

* 🔄 Testy integracyjne dla TokenService (wymaga mocków wszystkich zależności)
* 🔄 Testy dla AnafApiClient (wymaga mockowania HttpClient)
* 🔄 Testy dla TokenRepository (wymaga testowej bazy danych)
* 🔄 Testy wydajnościowe dla operacji na plikach


