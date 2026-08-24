using System.Diagnostics;
using AnafAutoToken.Manager.Services;

namespace AnafAutoToken.Manager.Forms;

internal sealed partial class MainForm
{
    private const string DefaultServiceName = "AnafAutoToken";
    private const string DefaultServiceDisplayName = "ANAF Auto Token Refresh Service";
    private const string DefaultServiceDescription = "Automatycznie odświeża tokeny ANAF przed wygaśnięciem";
    private const string WorkerExecutableName = "AnafAutoToken.Worker.exe";

    private readonly TextBox _serviceNameBox = new() { Text = DefaultServiceName };
    private readonly TextBox _serviceDisplayNameBox = new() { Text = DefaultServiceDisplayName };
    private readonly TextBox _serviceDescriptionBox = new() { Text = DefaultServiceDescription };
    private readonly TextBox _serviceBinaryPathBox = new();

    private readonly Label _serviceStatusLabel = new();
    private readonly Label _serviceDetailsLabel = new();
    private readonly Label _elevationLabel = new();
    private readonly Button _elevateButton = new() { Text = "Uruchom ponownie jako Administrator", AutoSize = true, Visible = false };

    private readonly Button _registerServiceButton = new() { Text = "Zarejestruj", Width = 130 };
    private readonly Button _unregisterServiceButton = new() { Text = "Wyrejestruj", Width = 130 };
    private readonly Button _startServiceButton = new() { Text = "Uruchom", Width = 130 };
    private readonly Button _stopServiceButton = new() { Text = "Zatrzymaj", Width = 130 };
    private readonly Button _restartServiceButton = new() { Text = "Restartuj", Width = 130 };
    private readonly Button _refreshServiceButton = new() { Text = "Odśwież", Width = 130 };

    private readonly Button _manualRefreshButton = new() { Text = "Odśwież token teraz", AutoSize = true };
    private readonly Label _manualRefreshEndpointLabel = new();
    private readonly Label _manualRefreshResultLabel = new();

    // 5 s, zgodnie z wymaganiem - odpytanie SCM jest tanie, więc odświeżamy stale.
    private readonly System.Windows.Forms.Timer _serviceStatusTimer = new() { Interval = 5000 };

    private ServiceSnapshot _serviceSnapshot = ServiceSnapshot.NotInstalled;
    private bool _serviceOperationInProgress;

    private TabPage BuildServiceTab()
    {
        var page = new TabPage("Serwis systemowy")
        {
            Padding = new Padding(12),
            UseVisualStyleBackColor = true
        };

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddSection(stack, BuildServiceStatusGroup());
        AddSection(stack, BuildServiceDefinitionGroup());
        AddSection(stack, BuildServiceActionsGroup());
        AddSection(stack, BuildManualRefreshGroup());

        host.Controls.Add(stack);

        page.Controls.Add(host);
        page.Controls.Add(BuildElevationBanner());

        _registerServiceButton.Click += async (_, _) => await RunServiceOperationAsync(
            "Rejestracja serwisu",
            () => WindowsServiceManager.Register(
                _serviceNameBox.Text.Trim(),
                _serviceDisplayNameBox.Text.Trim(),
                _serviceDescriptionBox.Text.Trim(),
                _serviceBinaryPathBox.Text.Trim()),
            confirmation: $"Zarejestrować serwis „{_serviceNameBox.Text.Trim()}”?");

        _unregisterServiceButton.Click += async (_, _) => await RunServiceOperationAsync(
            "Wyrejestrowanie serwisu",
            () =>
            {
                var serviceName = _serviceNameBox.Text.Trim();

                if (_serviceSnapshot.IsRunning)
                {
                    WindowsServiceManager.Stop(serviceName);
                }

                WindowsServiceManager.Unregister(serviceName);
            },
            confirmation: $"Wyrejestrować serwis „{_serviceNameBox.Text.Trim()}”? Jeśli działa, zostanie najpierw zatrzymany.");

        _startServiceButton.Click += async (_, _) => await RunServiceOperationAsync(
            "Uruchomienie serwisu",
            () => WindowsServiceManager.Start(_serviceNameBox.Text.Trim()));

        _stopServiceButton.Click += async (_, _) => await RunServiceOperationAsync(
            "Zatrzymanie serwisu",
            () => WindowsServiceManager.Stop(_serviceNameBox.Text.Trim()));

        _restartServiceButton.Click += async (_, _) => await RunServiceOperationAsync(
            "Restart serwisu",
            () => WindowsServiceManager.Restart(_serviceNameBox.Text.Trim()));

        _refreshServiceButton.Click += (_, _) => RefreshServiceStatus();
        _manualRefreshButton.Click += async (_, _) => await TriggerManualRefreshAsync();

        _serviceNameBox.TextChanged += (_, _) => RefreshServiceStatus();
        _serviceStatusTimer.Tick += (_, _) => RefreshServiceStatus();

        return page;
    }

    private Control BuildElevationBanner()
    {
        _elevationLabel.Dock = DockStyle.Fill;
        _elevationLabel.TextAlign = ContentAlignment.MiddleLeft;
        _elevationLabel.ForeColor = Color.Firebrick;
        _elevationLabel.AutoSize = false;

        _elevateButton.Dock = DockStyle.Fill;
        _elevateButton.Click += (_, _) => RestartElevated();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 34,
            Padding = new Padding(0, 0, 0, 6)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(_elevationLabel, 0, 0);
        layout.Controls.Add(_elevateButton, 1, 0);

        return layout;
    }

    private GroupBox BuildServiceStatusGroup()
    {
        _serviceStatusLabel.Dock = DockStyle.Top;
        _serviceStatusLabel.AutoSize = false;
        _serviceStatusLabel.Height = 40;
        _serviceStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _serviceStatusLabel.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        _serviceStatusLabel.Text = "…";

        _serviceDetailsLabel.Dock = DockStyle.Top;
        _serviceDetailsLabel.AutoSize = false;
        _serviceDetailsLabel.Height = 76;
        _serviceDetailsLabel.TextAlign = ContentAlignment.TopLeft;

        var content = new Panel
        {
            Dock = DockStyle.Top,
            Height = 120
        };

        content.Controls.Add(_serviceDetailsLabel);
        content.Controls.Add(_serviceStatusLabel);

        return CreateGroup("Stan (odświeżany co 5 sekund)", content);
    }

    private GroupBox BuildServiceDefinitionGroup()
    {
        var table = CreateFieldTable();

        AddField(table, "Nazwa serwisu", _serviceNameBox);
        AddField(table, "Nazwa wyświetlana", _serviceDisplayNameBox);
        AddField(table, "Opis", _serviceDescriptionBox);
        AddField(table, "Plik wykonywalny workera", _serviceBinaryPathBox, CreateBrowseFileButton(_serviceBinaryPathBox));

        return CreateGroup("Definicja serwisu", table);
    }

    private GroupBox BuildServiceActionsGroup()
    {
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        foreach (var button in new[]
                 {
                     _registerServiceButton,
                     _unregisterServiceButton,
                     _startServiceButton,
                     _stopServiceButton,
                     _restartServiceButton,
                     _refreshServiceButton
                 })
        {
            button.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(button);
        }

        return CreateGroup("Akcje", buttons);
    }

    private GroupBox BuildManualRefreshGroup()
    {
        _manualRefreshEndpointLabel.Dock = DockStyle.Top;
        _manualRefreshEndpointLabel.AutoSize = false;
        _manualRefreshEndpointLabel.Height = 22;
        _manualRefreshEndpointLabel.TextAlign = ContentAlignment.MiddleLeft;
        _manualRefreshEndpointLabel.ForeColor = SystemColors.GrayText;

        _manualRefreshResultLabel.Dock = DockStyle.Top;
        _manualRefreshResultLabel.AutoSize = false;
        _manualRefreshResultLabel.Height = 54;
        _manualRefreshResultLabel.TextAlign = ContentAlignment.TopLeft;
        _manualRefreshResultLabel.Text = "Jeszcze nie uruchamiano ręcznego odświeżenia w tej sesji.";
        _manualRefreshResultLabel.ForeColor = SystemColors.GrayText;

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 2, 0, 6)
        };

        buttonRow.Controls.Add(_manualRefreshButton);

        var content = new Panel
        {
            Dock = DockStyle.Top,
            Height = 120
        };

        content.Controls.Add(_manualRefreshResultLabel);
        content.Controls.Add(buttonRow);
        content.Controls.Add(_manualRefreshEndpointLabel);

        return CreateGroup("Ręczne odświeżenie tokenu", content);
    }

    /// <summary>
    /// Adres bierzemy z pola Api:Url na zakładce Konfiguracja, żeby nie trzymać tej samej
    /// wartości w dwóch miejscach interfejsu.
    /// </summary>
    private string ResolveWorkerApiUrl()
    {
        var url = _apiUrlBox.Text.Trim();
        return string.IsNullOrWhiteSpace(url) ? WorkerApiClient.DefaultBaseUrl : url;
    }

    private async Task TriggerManualRefreshAsync()
    {
        var endpoint = ResolveWorkerApiUrl();

        var answer = MessageBox.Show(
            this,
            "Wymusić odświeżenie tokenu teraz?" + Environment.NewLine + Environment.NewLine
            + "Worker wykona pełną procedurę: sprawdzi ważność access tokenu, w razie potrzeby odpyta "
            + "ANAF, zapisze nowy refresh token do bazy, zaktualizuje config.ini i wyśle powiadomienia e-mail."
            + Environment.NewLine + Environment.NewLine
            + $"Adres workera: {endpoint}",
            "Ręczne odświeżenie tokenu",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (answer != DialogResult.OK)
        {
            SetStatus("Ręczne odświeżenie tokenu: anulowano.");
            return;
        }

        _serviceOperationInProgress = true;
        _serviceStatusTimer.Stop();
        _manualRefreshButton.Enabled = false;
        UpdateServiceButtons();
        UseWaitCursor = true;
        SetStatus("Ręczne odświeżenie tokenu - czekam na odpowiedź workera…");
        SetManualRefreshResult("Trwa odświeżanie…", SystemColors.GrayText);

        try
        {
            var response = await WorkerApiClient.TriggerRefreshAsync(endpoint);
            var duration = response.CompletedAtUtc - response.StartedAtUtc;
            var expiration = response.NewExpirationDate is { } value
                ? $"{value:yyyy-MM-dd HH:mm:ss} UTC"
                : "nieznana";

            if (!response.IsSuccess)
            {
                SetManualRefreshResult(
                    $"Odświeżenie nie powiodło się: {response.ErrorMessage}"
                    + $"{Environment.NewLine}Czas: {duration.TotalSeconds:F1} s. Szczegóły w logach serwisu.",
                    Color.Firebrick);
                SetStatus("Ręczne odświeżenie tokenu zakończone błędem.", isWarning: true);
                return;
            }

            if (response.TokenWasRefreshed)
            {
                SetManualRefreshResult(
                    $"Token odświeżony. Nowy access token wygasa: {expiration}."
                    + $"{Environment.NewLine}Czas: {duration.TotalSeconds:F1} s.",
                    Color.SeaGreen);
                SetStatus("Token odświeżony - odświeżam podgląd bazy.");

                await ReloadDatabaseAsync();
            }
            else
            {
                SetManualRefreshResult(
                    $"Odświeżenie nie było potrzebne - access token jest jeszcze ważny do {expiration}."
                    + $"{Environment.NewLine}Czas: {duration.TotalSeconds:F1} s.",
                    Color.DarkGoldenrod);
                SetStatus("Worker uznał, że odświeżenie nie jest jeszcze potrzebne.");
            }
        }
        catch (Exception ex)
        {
            SetManualRefreshResult(ex.Message, Color.Firebrick);
            SetStatus($"Ręczne odświeżenie tokenu: {ex.Message}", isWarning: true);
            MessageBox.Show(this, ex.Message, "Ręczne odświeżenie tokenu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _serviceOperationInProgress = false;
            _manualRefreshButton.Enabled = true;
            RefreshServiceStatus();
            _serviceStatusTimer.Start();
        }
    }

    private void SetManualRefreshResult(string text, Color color)
    {
        _manualRefreshResultLabel.Text = $"[{DateTime.Now:HH:mm:ss}] {text}";
        _manualRefreshResultLabel.ForeColor = color;
    }

    private void InitialiseServiceTab()
    {
        var isElevated = WindowsServiceManager.IsElevated();

        _elevationLabel.Text = isElevated
            ? "Menedżer działa z uprawnieniami administratora."
            : "Menedżer działa bez uprawnień administratora - rejestracja, start i stop serwisu zakończą się błędem.";

        _elevationLabel.ForeColor = isElevated ? Color.SeaGreen : Color.Firebrick;
        _elevateButton.Visible = !isElevated;

        ApplyDefaultServiceBinaryPath();
        RefreshServiceStatus();
        _serviceStatusTimer.Start();
    }

    /// <summary>
    /// Worker zwykle leży obok pliku appsettings.json, którym operuje menedżer.
    /// Ścieżka wpisana ręcznie przez operatora nie jest nadpisywana.
    /// </summary>
    private void ApplyDefaultServiceBinaryPath()
    {
        if (_serviceBinaryPathBox.Modified && !string.IsNullOrWhiteSpace(_serviceBinaryPathBox.Text))
        {
            return;
        }

        var directory = SafeGetDirectory(_settingsPathBox.Text) ?? AppContext.BaseDirectory;
        _serviceBinaryPathBox.Text = Path.Combine(directory, WorkerExecutableName);
    }

    private void RefreshServiceStatus()
    {
        if (_serviceOperationInProgress)
        {
            return;
        }

        try
        {
            _serviceSnapshot = WindowsServiceManager.Query(_serviceNameBox.Text.Trim());
        }
        catch (Exception ex)
        {
            _serviceSnapshot = ServiceSnapshot.NotInstalled;
            SetStatus($"Nie udało się odczytać stanu serwisu: {ex.Message}", isWarning: true);
        }

        _serviceStatusLabel.Text = _serviceSnapshot.StatusText;
        _serviceStatusLabel.ForeColor = _serviceSnapshot switch
        {
            { IsInstalled: false } => Color.DimGray,
            { IsRunning: true } => Color.SeaGreen,
            { IsTransitioning: true } => Color.DarkGoldenrod,
            _ => Color.Firebrick
        };

        // Kolor detali pinujemy przy każdym odświeżeniu - tylko nagłówek stanu
        // ma być kolorowany, reszta zawsze czyta się jako tekst pomocniczy.
        _serviceDetailsLabel.ForeColor = SystemColors.GrayText;
        _manualRefreshEndpointLabel.Text = $"Żądanie trafi do: POST {ResolveWorkerApiUrl().TrimEnd('/')}/api/tokens/refresh";

        _serviceDetailsLabel.Text = _serviceSnapshot.IsInstalled
            ? string.Join(
                Environment.NewLine,
                $"Nazwa wyświetlana: {_serviceSnapshot.DisplayName}",
                $"Typ startu: {_serviceSnapshot.StartTypeText}",
                $"Plik z rejestru: {_serviceSnapshot.BinaryPath ?? "-"}",
                $"Sprawdzono: {DateTime.Now:HH:mm:ss}")
            : string.Join(
                Environment.NewLine,
                "Serwis o tej nazwie nie jest zarejestrowany w systemie.",
                "Uzupełnij definicję poniżej i kliknij „Zarejestruj”.",
                string.Empty,
                $"Sprawdzono: {DateTime.Now:HH:mm:ss}");

        UpdateServiceButtons();
    }

    private void UpdateServiceButtons()
    {
        var idle = !_serviceOperationInProgress;
        var installed = _serviceSnapshot.IsInstalled;

        _registerServiceButton.Enabled = idle && !installed;
        _unregisterServiceButton.Enabled = idle && installed;
        _startServiceButton.Enabled = idle && installed && !_serviceSnapshot.IsRunning && !_serviceSnapshot.IsTransitioning;
        _stopServiceButton.Enabled = idle && installed && _serviceSnapshot.IsRunning;
        _restartServiceButton.Enabled = idle && installed && _serviceSnapshot.IsRunning;
        _refreshServiceButton.Enabled = idle;
    }

    private async Task RunServiceOperationAsync(string title, Action operation, string? confirmation = null)
    {
        if (string.IsNullOrWhiteSpace(_serviceNameBox.Text))
        {
            SetStatus("Podaj nazwę serwisu.", isWarning: true);
            return;
        }

        if (confirmation is not null)
        {
            var answer = MessageBox.Show(this, confirmation, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (answer != DialogResult.OK)
            {
                SetStatus($"{title}: anulowano.");
                return;
            }
        }

        _serviceOperationInProgress = true;
        _serviceStatusTimer.Stop();
        UpdateServiceButtons();
        UseWaitCursor = true;
        SetStatus($"{title}…");

        try
        {
            await Task.Run(operation);
            SetStatus($"{title}: gotowe.");
        }
        catch (Exception ex)
        {
            SetStatus($"{title}: {ex.Message}", isWarning: true);
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _serviceOperationInProgress = false;
            RefreshServiceStatus();
            _serviceStatusTimer.Start();
        }
    }

    private void RestartElevated()
    {
        var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;

        try
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
            });

            Close();
        }
        catch (Exception ex)
        {
            // Najczęściej: operator odrzucił monit UAC.
            SetStatus($"Nie udało się uruchomić ponownie z uprawnieniami administratora: {ex.Message}", isWarning: true);
        }
    }
}
