#!/bin/bash

# ============================================================================
# Script instalacji serwisu systemd dla AnafAutoToken (Linux)
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

echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}Instalacja serwisu AnafAutoToken${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""

# Sprawdzenie uprawnień root
if [ "$EUID" -ne 0 ]; then 
    echo -e "${RED}Ten skrypt wymaga uprawnień root. Uruchom z sudo.${NC}"
    exit 1
fi

# Sprawdzenie instalacji .NET 8.0 Runtime
echo -e "${YELLOW}Sprawdzanie instalacji .NET 8.0 Runtime...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}.NET nie jest zainstalowany.${NC}"
    echo -e "${YELLOW}Instalacja .NET 8.0 Runtime...${NC}"
    
    # Detekcja dystrybucji
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        OS=$ID
        VER=$VERSION_ID
    else
        echo -e "${RED}Nie można wykryć dystrybucji Linux${NC}"
        exit 1
    fi
    
    case $OS in
        ubuntu|debian)
            wget https://packages.microsoft.com/config/$OS/$VER/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
            dpkg -i packages-microsoft-prod.deb
            rm packages-microsoft-prod.deb
            apt-get update
            apt-get install -y aspnetcore-runtime-8.0
            ;;
        rhel|centos|fedora)
            rpm -Uvh https://packages.microsoft.com/config/centos/8/packages-microsoft-prod.rpm
            dnf install -y aspnetcore-runtime-8.0
            ;;
        *)
            echo -e "${RED}Nieobsługiwana dystrybucja: $OS${NC}"
            echo -e "${YELLOW}Zainstaluj .NET 8.0 Runtime ręcznie: https://dotnet.microsoft.com/download/dotnet/8.0${NC}"
            exit 1
            ;;
    esac
fi

DOTNET_VERSION=$(dotnet --list-runtimes | grep "Microsoft.NETCore.App 8.0" || true)
if [ -z "$DOTNET_VERSION" ]; then
    echo -e "${RED}.NET 8.0 Runtime nie jest zainstalowany.${NC}"
    echo -e "${YELLOW}Pobierz z: https://dotnet.microsoft.com/download/dotnet/8.0${NC}"
    exit 1
fi
echo -e "${GREEN}✓ .NET 8.0 Runtime znaleziony${NC}"
echo ""

# Publikacja aplikacji
echo -e "${YELLOW}Publikowanie aplikacji...${NC}"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PUBLISH_DIR="$SCRIPT_DIR/bin/Release/net8.0/publish"

dotnet publish "$SCRIPT_DIR/AnafAutoToken.Worker/AnafAutoToken.Worker.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o "$PUBLISH_DIR"

echo -e "${GREEN}✓ Aplikacja opublikowana w: $PUBLISH_DIR${NC}"
echo ""

# Tworzenie użytkownika systemowego
echo -e "${YELLOW}Tworzenie użytkownika systemowego...${NC}"
if ! id "$SERVICE_USER" &>/dev/null; then
    useradd -r -s /bin/false -d "$INSTALL_DIR" -c "ANAF Token Service" "$SERVICE_USER"
    echo -e "${GREEN}✓ Utworzono użytkownika: $SERVICE_USER${NC}"
else
    echo -e "${GREEN}✓ Użytkownik istnieje: $SERVICE_USER${NC}"
fi
echo ""

# Tworzenie katalogów
echo -e "${YELLOW}Tworzenie katalogów instalacyjnych...${NC}"
mkdir -p "$INSTALL_DIR"
mkdir -p "$BACKUP_DIR"
mkdir -p "$LOG_DIR"

# Kopiowanie plików
echo -e "${YELLOW}Kopiowanie plików aplikacji...${NC}"
cp -r "$PUBLISH_DIR"/* "$INSTALL_DIR/"
echo -e "${GREEN}✓ Pliki skopiowane do: $INSTALL_DIR${NC}"
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
chown -R "$SERVICE_USER":"$SERVICE_USER" "$BACKUP_DIR"
chown -R "$SERVICE_USER":"$SERVICE_USER" "$LOG_DIR"
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
echo -e "${YELLOW}  Ustaw: AnafSettings:BasicAuth:Username i Password${NC}"
echo -e "${YELLOW}  Ustaw: AnafSettings:InitialRefreshToken (jeśli używasz)${NC}"
echo ""
