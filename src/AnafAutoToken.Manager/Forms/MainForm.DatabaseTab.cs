using System.Text.Json;
using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Manager.Data;
using AnafAutoToken.Shared.Configuration;

namespace AnafAutoToken.Manager.Forms;

internal sealed partial class MainForm
{
    private const string TokenDatabaseConnectionKey = "TokenDatabase";

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly TextBox _databasePathBox = new();
    private readonly Label _summaryLabel = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _accessTokenBox = new();
    private readonly TextBox _refreshTokenBox = new();

    private readonly SplitContainer _databaseSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        Panel1MinSize = 120,
        Panel2MinSize = 150
    };

    private IReadOnlyList<TokenLogRow> _rows = [];

    private TabPage BuildDatabaseTab()
    {
        var page = new TabPage("Baza danych")
        {
            Padding = new Padding(12),
            UseVisualStyleBackColor = true
        };

        _databaseSplit.Panel1.Controls.Add(BuildGrid());
        _databaseSplit.Panel2.Controls.Add(BuildTokenDetails());

        page.Controls.Add(_databaseSplit);
        page.Controls.Add(BuildDatabaseHeader());

        return page;
    }

    private Control BuildDatabaseHeader()
    {
        var browseButton = new Button { Text = "Przeglądaj…", Width = 110, Dock = DockStyle.Fill };
        var refreshButton = new Button { Text = "Odśwież", Width = 100, Dock = DockStyle.Fill };

        browseButton.Click += (_, _) => BrowseForDatabaseFile();
        refreshButton.Click += async (_, _) => await ReloadDatabaseAsync();

        _databasePathBox.Dock = DockStyle.Fill;

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.AutoSize = false;
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _summaryLabel.Text = "Brak wczytanych danych.";

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 2,
            Height = 66,
            Padding = new Padding(0, 0, 0, 6)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(new Label
        {
            Text = "Plik bazy (tokens.db):",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);

        layout.Controls.Add(_databasePathBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Controls.Add(refreshButton, 3, 0);
        layout.Controls.Add(_summaryLabel, 0, 1);
        layout.SetColumnSpan(_summaryLabel, 4);

        return layout;
    }

    private Control BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;

        AddGridColumn("Id", nameof(TokenGridRow.Id), 40);
        AddGridColumn("Zapisano (UTC)", nameof(TokenGridRow.SavedAt), 130);
        AddGridColumn("Status", nameof(TokenGridRow.Status), 60);
        AddGridColumn("Access token wygasa", nameof(TokenGridRow.AccessTokenExpiresAt), 130);
        AddGridColumn("Refresh token wygasa", nameof(TokenGridRow.RefreshTokenExpiresAt), 130);
        AddGridColumn("Access token", nameof(TokenGridRow.AccessTokenPreview), 150);
        AddGridColumn("Refresh token", nameof(TokenGridRow.RefreshTokenPreview), 150);
        AddGridColumn("HTTP", nameof(TokenGridRow.ResponseStatusCode), 50);
        AddGridColumn("Błąd", nameof(TokenGridRow.ErrorMessage), 220);

        _grid.SelectionChanged += (_, _) => ShowSelectedRowDetails();

        return _grid;
    }

    private void AddGridColumn(string header, string property, int fillWeight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private Control BuildTokenDetails()
    {
        ConfigureTokenBox(_accessTokenBox);
        ConfigureTokenBox(_refreshTokenBox);

        var copyAccessButton = new Button { Text = "Kopiuj access token", AutoSize = true, Dock = DockStyle.Fill };
        var copyRefreshButton = new Button { Text = "Kopiuj refresh token", AutoSize = true, Dock = DockStyle.Fill };

        copyAccessButton.Click += (_, _) => CopyToClipboard(_accessTokenBox.Text, "access token");
        copyRefreshButton.Click += (_, _) => CopyToClipboard(_refreshTokenBox.Text, "refresh token");

        var copyRowButton = new Button { Text = "Kopiuj zaznaczony wpis (JSON)", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var copyAllButton = new Button { Text = "Kopiuj całą historię (JSON)", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var saveJsonButton = new Button { Text = "Zapisz historię do pliku…", AutoSize = true };

        copyRowButton.Click += (_, _) => CopySelectedRowAsJson();
        copyAllButton.Click += (_, _) => CopyAllRowsAsJson();
        saveJsonButton.Click += (_, _) => SaveHistoryToFile();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true
        };

        buttons.Controls.Add(copyRowButton);
        buttons.Controls.Add(copyAllButton);
        buttons.Controls.Add(saveJsonButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(0, 6, 0, 0)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(CreateSectionLabel("Access token"), 0, 0);
        layout.Controls.Add(copyAccessButton, 1, 0);
        layout.Controls.Add(_accessTokenBox, 0, 1);
        layout.SetColumnSpan(_accessTokenBox, 2);

        layout.Controls.Add(CreateSectionLabel("Refresh token"), 0, 2);
        layout.Controls.Add(copyRefreshButton, 1, 2);
        layout.Controls.Add(_refreshTokenBox, 0, 3);
        layout.SetColumnSpan(_refreshTokenBox, 2);

        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);

        return layout;
    }

    private static Label CreateSectionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private static void ConfigureTokenBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.ReadOnly = true;
        box.WordWrap = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.BackColor = SystemColors.Window;
        box.Font = new Font("Consolas", 9f);
    }

    private void ApplyConfiguredDatabasePath()
    {
        // Ta sama reguła co w workerze: ścieżka względna rozwija się względem katalogu
        // danych, nigdy względem katalogu roboczego procesu.
        var connectionString = _document.GetString("ConnectionStrings", TokenDatabaseConnectionKey);

        _databasePathBox.Text = TokenDatabase.ResolveDatabasePath(connectionString);
    }

    private void BrowseForDatabaseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Wskaż plik bazy tokens.db",
            Filter = "Baza SQLite (*.db)|*.db|Wszystkie pliki (*.*)|*.*",
            FileName = Path.GetFileName(_databasePathBox.Text)
        };

        var directory = SafeGetDirectory(_databasePathBox.Text);

        if (directory is not null)
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _databasePathBox.Text = dialog.FileName;
        }
    }

    private async Task ReloadDatabaseAsync()
    {
        var path = _databasePathBox.Text;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Podaj ścieżkę do pliku tokens.db.", isWarning: true);
            return;
        }

        try
        {
            UseWaitCursor = true;
            _rows = await TokenDatabaseReader.ReadAllAsync(path);
            ApplyCheckRows(await TokenDatabaseReader.ReadChecksAsync(path));

            _grid.DataSource = _rows.Select(TokenGridRow.From).ToList();
            UpdateSummary();

            if (_grid.Rows.Count > 0)
            {
                // Land on the newest entry - the one the worker will actually use next.
                _grid.ClearSelection();
                _grid.CurrentCell = _grid.Rows[0].Cells[0];
                _grid.Rows[0].Selected = true;
            }

            ShowSelectedRowDetails();

            SetStatus($"Wczytano {_rows.Count} tokenów i {_checkRows.Count} przebiegów z {path}.");
        }
        catch (Exception ex)
        {
            _rows = [];
            _grid.DataSource = null;
            _summaryLabel.Text = "Brak wczytanych danych.";
            ApplyCheckRows([]);
            ShowError("Nie udało się odczytać bazy danych", ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void UpdateSummary()
    {
        var current = _rows.FirstOrDefault(row => row.IsSuccess && !string.IsNullOrWhiteSpace(row.RefreshToken));

        if (current is null)
        {
            _summaryLabel.Text = "W bazie nie ma jeszcze udanego odświeżenia - serwis użyje tokenu Anaf:InitialRefreshToken z konfiguracji.";
            return;
        }

        var refreshExpiry = current.RefreshTokenExpiresAt is { } expiry
            ? $"{FormatDate(expiry)} ({DescribeRemaining(expiry)})"
            : "nieznana";

        _summaryLabel.Text =
            $"Aktualny refresh token pochodzi z wpisu #{current.Id} zapisanego {FormatDate(current.CreatedAt)} UTC. "
            + $"Ważność refresh tokenu: {refreshExpiry}. "
            + $"Access token wygasa: {FormatDate(current.AccessTokenExpiresAt ?? current.ExpiresAt)}.";
    }

    private void ShowSelectedRowDetails()
    {
        var row = SelectedRow();

        _accessTokenBox.Text = row?.AccessToken ?? string.Empty;
        _refreshTokenBox.Text = row?.RefreshToken ?? string.Empty;
    }

    private TokenLogRow? SelectedRow()
    {
        if (_grid.CurrentRow?.DataBoundItem is TokenGridRow gridRow)
        {
            return gridRow.Source;
        }

        return null;
    }

    private void CopySelectedRowAsJson()
    {
        var row = SelectedRow();

        if (row is null)
        {
            SetStatus("Zaznacz wpis w tabeli.", isWarning: true);
            return;
        }

        CopyToClipboard(JsonSerializer.Serialize(ToExport(row), ExportJsonOptions), $"wpis #{row.Id} (JSON)");
    }

    private void CopyAllRowsAsJson()
    {
        if (_rows.Count == 0)
        {
            SetStatus("Brak danych do skopiowania.", isWarning: true);
            return;
        }

        CopyToClipboard(BuildHistoryJson(), $"historia {_rows.Count} wpisów (JSON)");
    }

    private void SaveHistoryToFile()
    {
        if (_rows.Count == 0)
        {
            SetStatus("Brak danych do zapisania.", isWarning: true);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Zapisz historię tokenów",
            Filter = "Pliki JSON (*.json)|*.json",
            FileName = $"anaf-tokens-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, BuildHistoryJson());
            SetStatus($"Zapisano historię do {dialog.FileName}. Plik zawiera aktywne tokeny - chroń go.");
        }
        catch (Exception ex)
        {
            ShowError("Nie udało się zapisać pliku", ex);
        }
    }

    private string BuildHistoryJson() => JsonSerializer.Serialize(
        new
        {
            ExportedAtUtc = DateTime.UtcNow,
            SourceDatabase = _databasePathBox.Text,
            Count = _rows.Count,
            TokenEntries = _rows.Select(ToExport).ToList()
        },
        ExportJsonOptions);

    private static object ToExport(TokenLogRow row) => new
    {
        row.Id,
        SavedAtUtc = row.CreatedAt,
        row.IsSuccess,
        row.AccessToken,
        AccessTokenExpiresAt = row.AccessTokenExpiresAt,
        StoredAccessTokenExpiresAt = row.ExpiresAt,
        row.RefreshToken,
        RefreshTokenExpiresAt = row.RefreshTokenExpiresAt,
        row.ResponseStatusCode,
        row.ErrorMessage
    };

    private static string FormatDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    private static string DescribeRemaining(DateTime expiry)
    {
        var days = (int)Math.Floor((expiry - DateTime.UtcNow).TotalDays);

        return days switch
        {
            < 0 => "termin minął",
            0 => "wygasa dziś",
            1 => "został 1 dzień",
            _ => $"zostało {days} dni"
        };
    }
}
