#!/bin/bash

# ============================================================================
# Script instalacji serwisu systemd dla AnafAutoToken (Linux)
#
# Aplikacja jest publikowana jako pojedynczy, samowystarczalny plik (self-contained,
# single file) - runtime .NET 10 jest w nim wkompilowany, więc na maszynie docelowej
# NIE trzeba instalować .NET.
#
# Tryby pracy:
#   sudo ./install-linux-service.sh                  # buduje na miejscu (wymaga SDK .NET 10)
#   sudo ./install-linux-service.sh --artifact PATH  # instaluje gotową paczkę (nie wymaga .NET)
#
# PATH może wskazywać na plik AnafAutoToken.Worker albo na katalog z paczką.
# ============================================================================

set -e  # Zatrzymaj przy pierwszym błędzie

# Kolory dla output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Konfiguracja
SERVICE_NAME="anaf-auto-token"
SERVICE_DISPLAY_NAME="ANAF Auto Token Refresh Service"
SERVICE_DESCRIPTION="Automatycznie odświeża tokeny ANAF przed wygaśnięciem"
INSTALL_DIR="/opt/anafautotoken"
CONFIG_FILE="$INSTALL_DIR/config.ini"
BACKUP_DIR="$INSTALL_DIR/backups"
LOG_DIR="$INSTALL_DIR/logs"
SERVICE_USER="anaftoken"
SERVICE_FILE="/etc/systemd/system/$SERVICE_NAME.service"
ARTIFACT_PATH=""

while [ $# -gt 0 ]; do
    case "$1" in
        --artifact)
            ARTIFACT_PATH="$2"
            shift 2
            ;;
        --artifact=*)
            ARTIFACT_PATH="${1#*=}"
            shift
            ;;
        *)
            echo -e "${RED}Nieznany parametr: $1${NC}"
            echo "Użycie: $0 [--artifact <ścieżka do pliku lub katalogu>]"
            exit 1
            ;;
    esac
done

echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}Instalacja serwisu AnafAutoToken${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""

# Sprawdzenie uprawnień root
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Ten skrypt wymaga uprawnień root. Uruchom z sudo.${NC}"
    exit 1
fi

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"

# Tworzenie katalogów
echo -e "${YELLOW}Tworzenie katalogów instalacyjnych...${NC}"
mkdir -p "$INSTALL_DIR"
mkdir -p "$BACKUP_DIR"
mkdir -p "$LOG_DIR"
echo ""

if [ -n "$ARTIFACT_PATH" ]; then
    # ------------------------------------------------------------------
    # Tryb 1: gotowa paczka - host nie potrzebuje .NET
    # ------------------------------------------------------------------
    echo -e "${YELLOW}Instalacja z gotowej paczki: $ARTIFACT_PATH${NC}"

    if [ ! -e "$ARTIFACT_PATH" ]; then
        echo -e "${RED}Nie znaleziono wskazanej paczki: $ARTIFACT_PATH${NC}"
        exit 1
    fi

    if [ -d "$ARTIFACT_PATH" ]; then
        if [ ! -f "$ARTIFACT_PATH/AnafAutoToken.Worker" ]; then
            echo -e "${RED}W katalogu $ARTIFACT_PATH nie ma pliku AnafAutoToken.Worker.${NC}"
            exit 1
        fi
        cp -r "$ARTIFACT_PATH"/* "$INSTALL_DIR/"
    else
        cp "$ARTIFACT_PATH" "$INSTALL_DIR/AnafAutoToken.Worker"
        echo -e "${YELLOW}⚠ Skopiowano sam plik binarny. Upewnij się, że w $INSTALL_DIR są też appsettings.json i katalog EmailTemplates.${NC}"
    fi

    echo -e "${GREEN}✓ Pliki skopiowane do: $INSTALL_DIR${NC}"
    echo ""
else
    # ------------------------------------------------------------------
    # Tryb 2: publikacja na miejscu - wymaga SDK .NET 10
    # ------------------------------------------------------------------
    echo -e "${YELLOW}Sprawdzanie SDK .NET 10...${NC}"

    if ! command -v dotnet &> /dev/null; then
        echo -e "${RED}Nie znaleziono polecenia 'dotnet'.${NC}"
        echo -e "${YELLOW}Zainstaluj SDK .NET 10 (https://dotnet.microsoft.com/download/dotnet/10.0)${NC}"
        echo -e "${YELLOW}albo uruchom skrypt z --artifact <gotowa paczka>.${NC}"
        exit 1
    fi

    if ! dotnet --list-sdks | grep -q "^10\."; then
        echo -e "${RED}Nie znaleziono SDK .NET 10.${NC}"
        echo -e "${YELLOW}Zainstaluj SDK .NET 10 albo uruchom skrypt z --artifact <gotowa paczka>.${NC}"
        exit 1
    fi

    echo -e "${GREEN}✓ SDK .NET 10 znalezione${NC}"
    echo ""

    case "$(uname -m)" in
        x86_64)  RUNTIME_ID="linux-x64" ;;
        aarch64) RUNTIME_ID="linux-arm64" ;;
        armv7l)  RUNTIME_ID="linux-arm" ;;
        *)
            echo -e "${RED}Nieobsługiwana architektura: $(uname -m)${NC}"
            exit 1
            ;;
    esac

    PUBLISH_DIR="$(mktemp -d)"
    trap 'rm -rf "$PUBLISH_DIR"' EXIT

    echo -e "${YELLOW}Publikowanie aplikacji ($RUNTIME_ID, self-contained, single file)...${NC}"

    dotnet publish "$REPO_ROOT/src/AnafAutoToken.Worker/AnafAutoToken.Worker.csproj" \
        -c Release \
        -r "$RUNTIME_ID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:SatelliteResourceLanguages=en \
        -p:DebugType=none \
        -o "$PUBLISH_DIR"

    echo -e "${GREEN}✓ Aplikacja opublikowana${NC}"
    echo ""

    echo -e "${YELLOW}Kopiowanie plików aplikacji...${NC}"
    cp -r "$PUBLISH_DIR"/* "$INSTALL_DIR/"
    echo -e "${GREEN}✓ Pliki skopiowane do: $INSTALL_DIR${NC}"
    echo ""
fi

if [ ! -f "$INSTALL_DIR/AnafAutoToken.Worker" ]; then
    echo -e "${RED}Nie znaleziono pliku wykonywalnego: $INSTALL_DIR/AnafAutoToken.Worker${NC}"
    exit 1
fi

# Tworzenie użytkownika systemowego
echo -e "${YELLOW}Tworzenie użytkownika systemowego...${NC}"
if ! id "$SERVICE_USER" &>/dev/null; then
    useradd -r -s /bin/false -d "$INSTALL_DIR" -c "ANAF Token Service" "$SERVICE_USER"
    echo -e "${GREEN}✓ Utworzono użytkownika: $SERVICE_USER${NC}"
else
    echo -e "${GREEN}✓ Użytkownik istnieje: $SERVICE_USER${NC}"
fi
echo ""

# Tworzenie przykładowego config.ini
if [ ! -f "$CONFIG_FILE" ]; then
    echo -e "${YELLOW}Tworzenie przykładowego config.ini...${NC}"
    cat > "$CONFIG_FILE" << 'EOF'
[AcessToken]
token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
refresh_token=your_initial_refresh_token_here
EOF
    echo -e "${GREEN}✓ Utworzono przykładowy config.ini${NC}"
    echo -e "${YELLOW}⚠ WAŻNE: Edytuj $CONFIG_FILE i wstaw prawdziwy refresh_token!${NC}"
    echo ""
fi

# Ustawienie uprawnień
echo -e "${YELLOW}Ustawianie uprawnień...${NC}"
chown -R "$SERVICE_USER":"$SERVICE_USER" "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/AnafAutoToken.Worker"
echo -e "${GREEN}✓ Uprawnienia ustawione${NC}"
echo ""

# Tworzenie pliku serwisu systemd
echo -e "${YELLOW}Tworzenie pliku serwisu systemd...${NC}"
cat > "$SERVICE_FILE" << EOF
[Unit]
Description=$SERVICE_DESCRIPTION
After=network.target

[Service]
Type=notify
User=$SERVICE_USER
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/AnafAutoToken.Worker
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$SERVICE_NAME
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOF

echo -e "${GREEN}✓ Plik serwisu utworzony: $SERVICE_FILE${NC}"
echo ""

# Przeładowanie systemd
echo -e "${YELLOW}Przeładowanie konfiguracji systemd...${NC}"
systemctl daemon-reload
echo -e "${GREEN}✓ systemd przeładowany${NC}"
echo ""

# Włączenie i uruchomienie serwisu
echo -e "${YELLOW}Włączanie serwisu (autostart)...${NC}"
systemctl enable "$SERVICE_NAME"
echo -e "${GREEN}✓ Serwis włączony (autostart)${NC}"
echo ""

echo -e "${YELLOW}Uruchamianie serwisu...${NC}"
systemctl start "$SERVICE_NAME"
sleep 3

# Sprawdzenie statusu
if systemctl is-active --quiet "$SERVICE_NAME"; then
    echo -e "${GREEN}✓ Serwis uruchomiony pomyślnie${NC}"
else
    echo -e "${YELLOW}⚠ Serwis zainstalowany ale nie uruchomiony${NC}"
    echo -e "${YELLOW}Sprawdź logi: journalctl -u $SERVICE_NAME -n 50${NC}"
fi
echo ""

# Podsumowanie
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}Instalacja zakończona!${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "Nazwa serwisu: ${GREEN}$SERVICE_NAME${NC}"
echo -e "Status: ${GREEN}$(systemctl is-active $SERVICE_NAME)${NC}"
echo -e "Autostart: ${GREEN}$(systemctl is-enabled $SERVICE_NAME)${NC}"
echo ""
echo -e "${GREEN}Runtime .NET jest wbudowany w plik AnafAutoToken.Worker - host nie wymaga instalacji .NET.${NC}"
echo ""
echo -e "Lokalizacje:"
echo -e "  Aplikacja: ${GRAY}$INSTALL_DIR${NC}"
echo -e "  Config:    ${GRAY}$CONFIG_FILE${NC}"
echo -e "  Backupy:   ${GRAY}$BACKUP_DIR${NC}"
echo -e "  Logi:      ${GRAY}$LOG_DIR${NC}"
echo -e "  Systemd:   ${GRAY}$SERVICE_FILE${NC}"
echo ""
echo -e "Przydatne komendy:"
echo -e "  Sprawdź status:  ${GRAY}systemctl status $SERVICE_NAME${NC}"
echo -e "  Zatrzymaj:       ${GRAY}systemctl stop $SERVICE_NAME${NC}"
echo -e "  Uruchom:         ${GRAY}systemctl start $SERVICE_NAME${NC}"
echo -e "  Restart:         ${GRAY}systemctl restart $SERVICE_NAME${NC}"
echo -e "  Zobacz logi:     ${GRAY}journalctl -u $SERVICE_NAME -f${NC}"
echo -e "  Wyłącz autostart:${GRAY}systemctl disable $SERVICE_NAME${NC}"
echo -e "  Odinstaluj:      ${GRAY}systemctl stop $SERVICE_NAME && systemctl disable $SERVICE_NAME && rm $SERVICE_FILE${NC}"
echo ""
echo -e "${YELLOW}⚠ Pamiętaj o edycji appsettings.json w katalogu $INSTALL_DIR!${NC}"
echo -e "${YELLOW}  Ustaw: Anaf:BasicAuth:Username i Password${NC}"
echo -e "${YELLOW}  Ustaw: Anaf:InitialRefreshToken (jeśli używasz)${NC}"
echo ""
