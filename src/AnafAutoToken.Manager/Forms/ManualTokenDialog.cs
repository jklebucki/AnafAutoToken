using AnafAutoToken.Shared.Extensions;

namespace AnafAutoToken.Manager.Forms;

/// <summary>
/// Pozwala wkleić aktualną parę tokenów - np. po ponownej autoryzacji w ANAF, gdy w bazie
/// nie ma jeszcze żadnego udanego odświeżenia albo zapisany refresh token przestał działać.
/// </summary>
internal sealed class ManualTokenDialog : Form
{
    private readonly TextBox _accessTokenBox = CreateTokenBox();
    private readonly TextBox _refreshTokenBox = CreateTokenBox();

    private readonly Label _accessTokenInfoLabel = new();
    private readonly Label _refreshTokenInfoLabel = new();
    private readonly Button _saveButton = new() { Text = "Zapisz", DialogResult = DialogResult.None, AutoSize = true };

    public ManualTokenDialog()
    {
        Text = "Wprowadź aktualne tokeny";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(860, 560);
        MinimumSize = new Size(700, 480);
        Font = new Font("Segoe UI", 9f);

        var cancelButton = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true };

        AcceptButton = _saveButton;
        CancelButton = cancelButton;

        _saveButton.Click += (_, _) => TrySave();
        _accessTokenBox.TextChanged += (_, _) => UpdateAccessTokenInfo();
        _refreshTokenBox.TextChanged += (_, _) => UpdateRefreshTokenInfo();

        ConfigureInfoLabel(_accessTokenInfoLabel);
        ConfigureInfoLabel(_refreshTokenInfoLabel);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12, 8, 12, 12)
        };

        cancelButton.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(_saveButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12, 12, 12, 0)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                 // wstęp
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));            // etykieta access
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));             // access
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));            // info access
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));            // etykieta refresh
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));             // refresh
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));            // info refresh

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 52,
            Text = "Wklej parę tokenów otrzymaną z ANAF. Zapis trafi do bazy jako wpis "
                 + "z oznaczeniem „Ręczne” i od tej chwili serwis użyje tego refresh tokenu "
                 + "przy następnym odświeżeniu.",
            ForeColor = SystemColors.GrayText
        }, 0, 0);

        layout.Controls.Add(CreateFieldLabel("Access token"), 0, 1);
        layout.Controls.Add(_accessTokenBox, 0, 2);
        layout.Controls.Add(_accessTokenInfoLabel, 0, 3);
        layout.Controls.Add(CreateFieldLabel("Refresh token"), 0, 4);
        layout.Controls.Add(_refreshTokenBox, 0, 5);
        layout.Controls.Add(_refreshTokenInfoLabel, 0, 6);

        Controls.Add(layout);
        Controls.Add(buttons);

        UpdateAccessTokenInfo();
        UpdateRefreshTokenInfo();
    }

    public string AccessToken { get; private set; } = string.Empty;

    public string RefreshToken { get; private set; } = string.Empty;

    /// <summary>Data wygaśnięcia odczytana z access tokenu.</summary>
    public DateTime AccessTokenExpiresAt { get; private set; }

    /// <summary>
    /// Data wygaśnięcia refresh tokenu, jeśli da się ją odczytać. Refresh tokeny ANAF nie
    /// zawsze są JWT, więc <c>null</c> jest normalnym wynikiem.
    /// </summary>
    public DateTime? RefreshTokenExpiresAt { get; private set; }

    /// <summary>Bez Dock pole nie wypełnia komórki i zostaje wielkości domyślnej.</summary>
    private static TextBox CreateTokenBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        Font = new Font("Consolas", 9f)
    };

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private static void ConfigureInfoLabel(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void UpdateAccessTokenInfo()
    {
        var token = _accessTokenBox.Text.Trim();

        if (token.Length == 0)
        {
            SetInfo(_accessTokenInfoLabel, "Pole wymagane.", SystemColors.GrayText);
            return;
        }

        if (token.GetExpirationDate() is not { } expiresAt)
        {
            SetInfo(
                _accessTokenInfoLabel,
                "To nie jest czytelny token JWT - serwis nie policzy z niego daty wygaśnięcia.",
                Color.Firebrick);
            return;
        }

        var daysLeft = (expiresAt - DateTime.UtcNow).TotalDays;

        SetInfo(
            _accessTokenInfoLabel,
            daysLeft <= 0
                ? $"Uwaga: token wygasł {expiresAt:yyyy-MM-dd HH:mm:ss} UTC."
                : $"Wygasa {expiresAt:yyyy-MM-dd HH:mm:ss} UTC (za {(int)daysLeft} dni).",
            daysLeft <= 0 ? Color.Firebrick : Color.SeaGreen);
    }

    private void UpdateRefreshTokenInfo()
    {
        var token = _refreshTokenBox.Text.Trim();

        if (token.Length == 0)
        {
            SetInfo(_refreshTokenInfoLabel, "Pole wymagane.", SystemColors.GrayText);
            return;
        }

        SetInfo(
            _refreshTokenInfoLabel,
            token.GetExpirationDate() is { } expiresAt
                ? $"Wygasa {expiresAt:yyyy-MM-dd HH:mm:ss} UTC."
                : $"Długość {token.Length} znaków. Data wygaśnięcia nieczytelna z tokenu - "
                  + "zostanie przyjęty rok od dziś.",
            SystemColors.GrayText);
    }

    private static void SetInfo(Label label, string text, Color color)
    {
        label.Text = text;
        label.ForeColor = color;
    }

    private void TrySave()
    {
        var accessToken = _accessTokenBox.Text.Trim();
        var refreshToken = _refreshTokenBox.Text.Trim();

        if (accessToken.Length == 0 || refreshToken.Length == 0)
        {
            Warn("Podaj oba tokeny.");
            return;
        }

        // Serwis liczy termin odświeżenia z daty wygaśnięcia access tokenu. Bez czytelnego
        // JWT nie miałby z czego, więc nie pozwalamy zapisać takiej pary.
        if (accessToken.GetExpirationDate() is not { } accessTokenExpiresAt)
        {
            Warn("Access token nie jest czytelnym tokenem JWT - serwis nie policzy z niego "
                 + "daty wygaśnięcia. Sprawdź, czy wklejona wartość jest kompletna.");
            return;
        }

        if (accessTokenExpiresAt <= DateTime.UtcNow)
        {
            var answer = MessageBox.Show(
                this,
                $"Access token wygasł {accessTokenExpiresAt:yyyy-MM-dd HH:mm:ss} UTC."
                + $"{Environment.NewLine}{Environment.NewLine}Zapisać mimo to?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        RefreshTokenExpiresAt = refreshToken.GetExpirationDate();

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
