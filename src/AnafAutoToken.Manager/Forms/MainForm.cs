using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Manager.Configuration;
using AnafAutoToken.Shared.Configuration;

namespace AnafAutoToken.Manager.Forms;

/// <summary>
/// Companion tool for the ANAF Auto Token service: shows what the worker stored in SQLite,
/// lets the operator copy those values out, and edits every key of <c>appsettings.json</c>.
/// </summary>
internal sealed partial class MainForm : Form
{
    private readonly TextBox _settingsPathBox = new();
    private readonly TabControl _tabs = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    private AppSettingsDocument _document = AppSettingsDocument.CreateEmpty();

    public MainForm()
    {
        Text = "ANAF Auto Token - Menedżer";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 820);
        MinimumSize = new Size(940, 620);
        Font = new Font("Segoe UI", 9f);

        // Taken from the executable itself so a single-file publish needs no loose .ico
        // next to it. The icon is cosmetic - failing to load one must not block startup.
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch (Exception)
        {
            Icon = null;
        }

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(12, 6);
        _tabs.TabPages.Add(BuildDatabaseTab());
        _tabs.TabPages.Add(BuildChecksTab());
        _tabs.TabPages.Add(BuildServiceTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.TabPages.Add(BuildRawJsonTab());

        Controls.Add(_tabs);
        Controls.Add(BuildSettingsFileBar());
        Controls.Add(BuildStatusBar());

        Load += async (_, _) => await LoadInitialStateAsync();
    }

    private Control BuildSettingsFileBar()
    {
        var browseButton = new Button { Text = "Przeglądaj…", Width = 110, Dock = DockStyle.Fill };
        var reloadButton = new Button { Text = "Wczytaj", Width = 100, Dock = DockStyle.Fill };
        var saveButton = new Button { Text = "Zapisz", Width = 100, Dock = DockStyle.Fill };

        browseButton.Click += (_, _) => BrowseForSettingsFile();
        reloadButton.Click += (_, _) => LoadSettings(_settingsPathBox.Text);
        saveButton.Click += (_, _) => SaveSettings();

        _settingsPathBox.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 5,
            RowCount = 1,
            Height = 44,
            Padding = new Padding(12, 9, 12, 6)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "Plik appsettings.json:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);

        layout.Controls.Add(_settingsPathBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Controls.Add(reloadButton, 3, 0);
        layout.Controls.Add(saveButton, 4, 0);

        return layout;
    }

    private Control BuildStatusBar()
    {
        var strip = new StatusStrip();
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        strip.Items.Add(_statusLabel);
        return strip;
    }

    private async Task LoadInitialStateAsync()
    {
        // The splitter cannot be positioned before the form has its real height.
        try
        {
            _databaseSplit.SplitterDistance = Math.Max(_databaseSplit.Panel1MinSize, (int)(_databaseSplit.Height * 0.45));
        }
        catch (InvalidOperationException)
        {
            // Window too small for the requested split - the default position is fine.
        }

        var bootstrapMessage = await BootstrapDataDirectoryAsync();

        LoadSettings(AppPaths.SettingsFile);
        InitialiseServiceTab();

        if (File.Exists(_databasePathBox.Text))
        {
            await ReloadDatabaseAsync();
        }

        // Komunikat bootstrapu pokazujemy tylko wtedy, gdy faktycznie coś powstało -
        // w kolejnych uruchomieniach ciekawsze jest podsumowanie wczytanych danych.
        if (bootstrapMessage is not null)
        {
            SetStatus(bootstrapMessage);
        }
    }

    /// <summary>
    /// Pierwsze uruchomienie menedżera zakłada komplet potrzebnych rzeczy w katalogu
    /// danych: podkatalogi, appsettings.json i bazę z nałożonymi migracjami. Dzięki temu
    /// usługa może wystartować od razu po instalacji, bez ręcznego przygotowywania plików.
    /// </summary>
    private async Task<string?> BootstrapDataDirectoryAsync()
    {
        try
        {
            var bootstrap = AppDataBootstrapper.Ensure();
            var createdDatabase = !File.Exists(AppPaths.DatabaseFile);

            await TokenDatabase.EnsureCreatedAsync(AppPaths.DefaultConnectionString);

            var created = new List<string>();

            if (bootstrap.CreatedDataDirectory)
            {
                created.Add("katalog danych");
            }

            if (bootstrap.CreatedSettingsFile)
            {
                created.Add(bootstrap.SeededFrom is null
                    ? "appsettings.json (z domyślnego wzorca)"
                    : "appsettings.json (na podstawie pliku z katalogu programu)");
            }

            if (createdDatabase)
            {
                created.Add("tokens.db");
            }

            return created.Count == 0
                ? null
                : $"Przygotowano w {AppPaths.DataDirectory}: {string.Join(", ", created)}.";
        }
        catch (Exception ex)
        {
            ShowError($"Nie udało się przygotować katalogu danych {AppPaths.DataDirectory}", ex);
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _serviceStatusTimer.Stop();
            _serviceStatusTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BrowseForSettingsFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Wskaż plik appsettings.json",
            Filter = "Pliki JSON (*.json)|*.json|Wszystkie pliki (*.*)|*.*",
            FileName = Path.GetFileName(_settingsPathBox.Text)
        };

        var directory = SafeGetDirectory(_settingsPathBox.Text);

        if (directory is not null)
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settingsPathBox.Text = dialog.FileName;
            LoadSettings(dialog.FileName);
        }
    }

    private void LoadSettings(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Podaj ścieżkę do pliku appsettings.json.", isWarning: true);
            return;
        }

        try
        {
            _document = AppSettingsDocument.Load(path);
            _settingsPathBox.Text = path;
            ApplyDocumentToSettingsForm();
            RefreshRawJsonView();
            ApplyConfiguredDatabasePath();
            SetStatus($"Wczytano konfigurację z {path}.");
        }
        catch (Exception ex)
        {
            ShowError("Nie udało się wczytać konfiguracji", ex);
        }
    }

    private void SaveSettings()
    {
        var path = _settingsPathBox.Text;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Podaj ścieżkę do pliku appsettings.json.", isWarning: true);
            return;
        }

        try
        {
            ApplySettingsFormToDocument();

            var answer = MessageBox.Show(
                this,
                $"Zapisać konfigurację do:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}"
                + "Kopia zapasowa poprzedniej wersji zostanie utworzona obok pliku."
                + $"{Environment.NewLine}Serwis wczytuje ustawienia przy starcie - po zapisie zrestartuj usługę.",
                "Zapis konfiguracji",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (answer != DialogResult.OK)
            {
                SetStatus("Zapis anulowany.");
                return;
            }

            var backupPath = _document.Save(path, createBackup: true);
            RefreshRawJsonView();
            ApplyConfiguredDatabasePath();

            SetStatus(backupPath is null
                ? $"Zapisano konfigurację do {path}."
                : $"Zapisano konfigurację do {path}. Kopia zapasowa: {Path.GetFileName(backupPath)}.");
        }
        catch (Exception ex)
        {
            ShowError("Nie udało się zapisać konfiguracji", ex);
        }
    }

    private const int ActionButtonWidth = 200;
    private const int ActionButtonHeight = 30;
    private const int ActionButtonSpacing = 8;
    private const int ActionBarTopPadding = 8;

    /// <summary>Wysokość paska akcji - taka sama na każdej zakładce.</summary>
    private static int ActionBarHeight => ActionButtonHeight + ActionBarTopPadding;

    /// <summary>
    /// Pasek akcji osadzony na siatce o stałych komórkach. Każdy przycisk dostaje tę samą
    /// szerokość i odstęp, więc dolne krawędzie zakładek wyglądają identycznie niezależnie
    /// od długości etykiet. Ostatnia kolumna wchłania resztę szerokości, żeby przyciski
    /// trzymały się lewej także po rozciągnięciu okna.
    /// </summary>
    private static TableLayoutPanel CreateActionBar(params Button[] buttons)
    {
        // Stała szerokość ustępuje tylko wtedy, gdy etykieta by się nie zmieściła -
        // przy większym DPI albo innym rozmiarze czcionki systemowej.
        var buttonWidth = Math.Max(
            ActionButtonWidth,
            buttons.Max(button => button.PreferredSize.Width + 2 * ActionButtonSpacing));

        var bar = new TableLayoutPanel
        {
            ColumnCount = buttons.Length + 1,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0, ActionBarTopPadding, 0, 0)
        };

        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionButtonHeight));

        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, ActionButtonSpacing, 0);

            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, buttonWidth + ActionButtonSpacing));
            bar.Controls.Add(button, index, 0);
        }

        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        return bar;
    }

    private static string? SafeGetDirectory(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return Directory.Exists(directory) ? directory : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void CopyToClipboard(string? text, string what)
    {
        if (string.IsNullOrEmpty(text))
        {
            SetStatus($"{what}: brak danych do skopiowania.", isWarning: true);
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus($"Skopiowano do schowka: {what} ({text.Length} znaków).");
        }
        catch (Exception ex)
        {
            ShowError("Nie udało się skopiować do schowka", ex);
        }
    }

    private void SetStatus(string message, bool isWarning = false)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isWarning ? Color.Firebrick : SystemColors.ControlText;
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus($"{title}: {exception.Message}", isWarning: true);
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
