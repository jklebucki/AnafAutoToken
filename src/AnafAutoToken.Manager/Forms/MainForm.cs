using AnafAutoToken.Manager.Configuration;

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

        try
        {
            Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "autoanaf.ico"));
        }
        catch (Exception)
        {
            // The icon is cosmetic - a missing file must not stop the tool from starting.
        }

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(12, 6);
        _tabs.TabPages.Add(BuildDatabaseTab());
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

        _settingsPathBox.Text = ResolveDefaultSettingsPath();

        if (File.Exists(_settingsPathBox.Text))
        {
            LoadSettings(_settingsPathBox.Text);
        }
        else
        {
            _settingsPathBox.Text = string.Empty;
            ApplyConfiguredDatabasePath();
            SetStatus(
                "Nie znaleziono pliku appsettings.json obok programu. Wskaż plik ustawień serwisu i kliknij Wczytaj.",
                isWarning: true);
        }

        if (File.Exists(_databasePathBox.Text))
        {
            await ReloadDatabaseAsync();
        }
    }

    private static string ResolveDefaultSettingsPath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
        ];

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
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
