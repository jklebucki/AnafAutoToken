using AnafAutoToken.Manager.Data;

namespace AnafAutoToken.Manager.Forms;

internal sealed partial class MainForm
{
    private readonly DataGridView _checksGrid = new();
    private readonly Label _checksSummaryLabel = new();

    private IReadOnlyList<TokenCheckRow> _checkRows = [];

    private TabPage BuildChecksTab()
    {
        var page = new TabPage("Historia sprawdzeń")
        {
            Padding = new Padding(12),
            UseVisualStyleBackColor = true
        };

        _checksGrid.Dock = DockStyle.Fill;
        _checksGrid.AutoGenerateColumns = false;
        _checksGrid.AllowUserToAddRows = false;
        _checksGrid.AllowUserToDeleteRows = false;
        _checksGrid.ReadOnly = true;
        _checksGrid.MultiSelect = false;
        _checksGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _checksGrid.RowHeadersVisible = false;
        _checksGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _checksGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;

        AddChecksColumn("Id", nameof(TokenCheckGridRow.Id), 40);
        AddChecksColumn("Sprawdzono (UTC)", nameof(TokenCheckGridRow.CheckedAt), 130);
        AddChecksColumn("Wynik", nameof(TokenCheckGridRow.Outcome), 120);
        AddChecksColumn("Wyzwalacz", nameof(TokenCheckGridRow.Trigger), 100);
        AddChecksColumn("Access token wygasa", nameof(TokenCheckGridRow.AccessTokenExpiresAt), 130);
        AddChecksColumn("Refresh token wygasa", nameof(TokenCheckGridRow.RefreshTokenExpiresAt), 130);
        AddChecksColumn("Komunikat", nameof(TokenCheckGridRow.Message), 300);

        _checksSummaryLabel.Dock = DockStyle.Top;
        _checksSummaryLabel.AutoSize = false;
        _checksSummaryLabel.Height = 46;
        _checksSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _checksSummaryLabel.Text = "Brak wczytanych danych.";

        var copyButton = new Button { Text = "Kopiuj historię (CSV)" };
        var refreshButton = new Button { Text = "Odśwież" };

        copyButton.Click += (_, _) => CopyChecksAsCsv();
        refreshButton.Click += async (_, _) => await ReloadDatabaseAsync();

        var buttons = CreateActionBar(copyButton, refreshButton);
        buttons.Dock = DockStyle.Bottom;
        buttons.Height = ActionBarHeight;

        page.Controls.Add(_checksGrid);
        page.Controls.Add(buttons);
        page.Controls.Add(_checksSummaryLabel);

        return page;
    }

    private void AddChecksColumn(string header, string property, int fillWeight) =>
        _checksGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private void ApplyCheckRows(IReadOnlyList<TokenCheckRow> rows)
    {
        _checkRows = rows;
        _checksGrid.DataSource = rows.Select(TokenCheckGridRow.From).ToList();

        if (rows.Count == 0)
        {
            _checksSummaryLabel.Text =
                "Brak zapisanych przebiegów. Tabela TokenCheckLogs zapełnia się przy każdym sprawdzeniu tokenu - "
                + "jeśli jest pusta, serwis jeszcze się nie uruchomił albo pochodzi sprzed tej wersji.";
            return;
        }

        var newest = rows[0];
        var refreshed = rows.Count(row => row.Outcome == 1);
        var failed = rows.Count(row => row.Outcome == 2);

        _checksSummaryLabel.Text =
            $"Przebiegów: {rows.Count} (odświeżeń: {refreshed}, błędów: {failed}). "
            + $"Ostatni: {newest.CheckedAt:yyyy-MM-dd HH:mm:ss} UTC - {newest.OutcomeText} ({newest.TriggerText}).";
    }

    private void CopyChecksAsCsv()
    {
        if (_checkRows.Count == 0)
        {
            SetStatus("Brak danych do skopiowania.", isWarning: true);
            return;
        }

        var lines = new List<string>
        {
            "Id;SprawdzonoUtc;Wynik;Wyzwalacz;AccessTokenWygasa;RefreshTokenWygasa;Komunikat"
        };

        lines.AddRange(_checkRows.Select(row => string.Join(';',
            row.Id,
            row.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            row.OutcomeText,
            row.TriggerText,
            row.AccessTokenExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
            row.RefreshTokenExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
            row.Message?.ReplaceLineEndings(" ").Replace(';', ',') ?? string.Empty)));

        CopyToClipboard(string.Join(Environment.NewLine, lines), $"historia {_checkRows.Count} przebiegów (CSV)");
    }
}
