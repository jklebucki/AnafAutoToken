using AnafAutoToken.Manager.Configuration;

namespace AnafAutoToken.Manager.Forms;

internal sealed partial class MainForm
{
    private static readonly string[] LogLevels =
    [
        "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"
    ];

    private readonly TextBox _tokenEndpointBox = new();
    private readonly TextBox _basicAuthUsernameBox = new();
    private readonly TextBox _basicAuthPasswordBox = new() { UseSystemPasswordChar = true };
    private readonly NumericUpDown _checkHourUpDown = new() { Minimum = 0, Maximum = 23 };
    private readonly NumericUpDown _checkMinuteUpDown = new() { Minimum = 0, Maximum = 59 };
    private readonly NumericUpDown _daysBeforeExpirationUpDown = new() { Minimum = 1, Maximum = 365 };
    private readonly TextBox _configFilePathBox = new();
    private readonly TextBox _backupDirectoryBox = new();
    private readonly TextBox _initialRefreshTokenBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    private readonly TextBox _smtpServerBox = new();
    private readonly NumericUpDown _smtpPortUpDown = new() { Minimum = 1, Maximum = 65535 };
    private readonly TextBox _smtpUsernameBox = new();
    private readonly TextBox _smtpPasswordBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _fromAddressBox = new();
    private readonly TextBox _fromNameBox = new();
    private readonly TextBox _toAddressesBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _enableSslCheck = new() { Text = "Używaj SSL/TLS", AutoSize = true };

    private readonly TextBox _connectionStringBox = new();
    private readonly TextBox _apiUrlBox = new();

    private readonly ComboBox _logDefaultCombo = CreateLogLevelCombo();
    private readonly ComboBox _logHostingLifetimeCombo = CreateLogLevelCombo();
    private readonly ComboBox _logEfCoreCombo = CreateLogLevelCombo();

    private readonly CheckBox _showSecretsCheck = new() { Text = "Pokaż hasła i sekrety", AutoSize = true };

    private readonly TextBox _rawJsonBox = new();

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Konfiguracja")
        {
            Padding = new Padding(12),
            UseVisualStyleBackColor = true
        };

        _showSecretsCheck.CheckedChanged += (_, _) =>
        {
            _basicAuthPasswordBox.UseSystemPasswordChar = !_showSecretsCheck.Checked;
            _smtpPasswordBox.UseSystemPasswordChar = !_showSecretsCheck.Checked;
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

        AddSection(stack, BuildAnafGroup());
        AddSection(stack, BuildEmailGroup());
        AddSection(stack, BuildRuntimeGroup());

        host.Controls.Add(stack);

        page.Controls.Add(host);
        page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 30, Controls = { _showSecretsCheck } });

        return page;
    }

    private GroupBox BuildAnafGroup()
    {
        var table = CreateFieldTable();

        AddField(table, "Adres endpointu tokenu", _tokenEndpointBox);
        AddField(table, "Basic auth - użytkownik", _basicAuthUsernameBox);
        AddField(table, "Basic auth - hasło", _basicAuthPasswordBox);
        AddField(table, "Godzina sprawdzania", _checkHourUpDown);
        AddField(table, "Minuta sprawdzania", _checkMinuteUpDown);
        AddField(table, "Dni przed wygaśnięciem", _daysBeforeExpirationUpDown);
        AddField(table, "Plik config.ini", _configFilePathBox, CreateBrowseFileButton(_configFilePathBox));
        AddField(table, "Katalog kopii zapasowych", _backupDirectoryBox, CreateBrowseFolderButton(_backupDirectoryBox));
        AddField(table, "Początkowy refresh token", _initialRefreshTokenBox, height: 70);

        return CreateGroup("ANAF", table);
    }

    private GroupBox BuildEmailGroup()
    {
        var table = CreateFieldTable();

        AddField(table, "Serwer SMTP", _smtpServerBox);
        AddField(table, "Port SMTP", _smtpPortUpDown);
        AddField(table, "Użytkownik SMTP", _smtpUsernameBox);
        AddField(table, "Hasło SMTP", _smtpPasswordBox);
        AddField(table, "Adres nadawcy", _fromAddressBox);
        AddField(table, "Nazwa nadawcy", _fromNameBox);
        AddField(table, "Odbiorcy (jeden na wiersz)", _toAddressesBox, height: 70);
        AddField(table, string.Empty, _enableSslCheck);

        return CreateGroup("Powiadomienia e-mail", table);
    }

    private GroupBox BuildRuntimeGroup()
    {
        var table = CreateFieldTable();

        AddField(table, "Connection string bazy", _connectionStringBox);
        AddField(table, "Adres API workera", _apiUrlBox);
        AddField(table, "Log - poziom domyślny", _logDefaultCombo);
        AddField(table, "Log - Microsoft.Hosting.Lifetime", _logHostingLifetimeCombo);
        AddField(table, "Log - Microsoft.EntityFrameworkCore", _logEfCoreCombo);

        return CreateGroup("Baza danych, API i logowanie", table);
    }

    private TabPage BuildRawJsonTab()
    {
        var page = new TabPage("JSON (surowy)")
        {
            Padding = new Padding(12),
            UseVisualStyleBackColor = true
        };

        _rawJsonBox.Dock = DockStyle.Fill;
        _rawJsonBox.Multiline = true;
        _rawJsonBox.WordWrap = false;
        _rawJsonBox.ScrollBars = ScrollBars.Both;
        _rawJsonBox.AcceptsTab = true;
        _rawJsonBox.Font = new Font("Consolas", 9.5f);

        var fromFormButton = new Button { Text = "Pokaż stan formularza", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var toFormButton = new Button { Text = "Zastosuj JSON do formularza", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var copyButton = new Button { Text = "Kopiuj JSON", AutoSize = true };

        fromFormButton.Click += (_, _) =>
        {
            ApplySettingsFormToDocument();
            RefreshRawJsonView();
            SetStatus("Podgląd JSON odświeżony na podstawie formularza.");
        };

        toFormButton.Click += (_, _) => ApplyRawJsonToForm();
        copyButton.Click += (_, _) => CopyToClipboard(_rawJsonBox.Text, "zawartość appsettings.json");

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };

        buttons.Controls.Add(fromFormButton);
        buttons.Controls.Add(toFormButton);
        buttons.Controls.Add(copyButton);

        page.Controls.Add(_rawJsonBox);
        page.Controls.Add(buttons);
        page.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Pełna zawartość pliku - także klucze, których nie ma na zakładce Konfiguracja. "
                 + "Zapis odbywa się przyciskiem Zapisz na górze okna."
        });

        return page;
    }

    private void ApplyDocumentToSettingsForm()
    {
        _tokenEndpointBox.Text = _document.GetString("Anaf", "TokenEndpoint") ?? string.Empty;
        _basicAuthUsernameBox.Text = _document.GetString("Anaf", "BasicAuth", "Username") ?? string.Empty;
        _basicAuthPasswordBox.Text = _document.GetString("Anaf", "BasicAuth", "Password") ?? string.Empty;

        SetNumeric(_checkHourUpDown, _document.GetInt("Anaf", "CheckSchedule", "CheckHour") ?? 12);
        SetNumeric(_checkMinuteUpDown, _document.GetInt("Anaf", "CheckSchedule", "CheckMinute") ?? 0);
        SetNumeric(_daysBeforeExpirationUpDown, _document.GetInt("Anaf", "DaysBeforeExpiration") ?? 3);

        _configFilePathBox.Text = _document.GetString("Anaf", "ConfigFilePath") ?? string.Empty;
        _backupDirectoryBox.Text = _document.GetString("Anaf", "BackupDirectory") ?? string.Empty;
        _initialRefreshTokenBox.Text = _document.GetString("Anaf", "InitialRefreshToken") ?? string.Empty;

        _smtpServerBox.Text = _document.GetString("Anaf", "Email", "SmtpServer") ?? string.Empty;
        SetNumeric(_smtpPortUpDown, _document.GetInt("Anaf", "Email", "SmtpPort") ?? 587);
        _smtpUsernameBox.Text = _document.GetString("Anaf", "Email", "Username") ?? string.Empty;
        _smtpPasswordBox.Text = _document.GetString("Anaf", "Email", "Password") ?? string.Empty;
        _fromAddressBox.Text = _document.GetString("Anaf", "Email", "FromAddress") ?? string.Empty;
        _fromNameBox.Text = _document.GetString("Anaf", "Email", "FromName") ?? string.Empty;
        _toAddressesBox.Lines = _document.GetStringArray("Anaf", "Email", "ToAddresses").ToArray();
        _enableSslCheck.Checked = _document.GetBool("Anaf", "Email", "EnableSsl") ?? true;

        _connectionStringBox.Text = _document.GetString("ConnectionStrings", TokenDatabaseConnectionKey) ?? string.Empty;
        _apiUrlBox.Text = _document.GetString("Api", "Url") ?? string.Empty;

        _logDefaultCombo.Text = _document.GetString("Logging", "LogLevel", "Default") ?? "Information";
        _logHostingLifetimeCombo.Text = _document.GetString("Logging", "LogLevel", "Microsoft.Hosting.Lifetime") ?? "Information";
        _logEfCoreCombo.Text = _document.GetString("Logging", "LogLevel", "Microsoft.EntityFrameworkCore") ?? "Warning";
    }

    private void ApplySettingsFormToDocument()
    {
        _document.SetString(_tokenEndpointBox.Text.Trim(), "Anaf", "TokenEndpoint");
        _document.SetString(_basicAuthUsernameBox.Text.Trim(), "Anaf", "BasicAuth", "Username");
        _document.SetString(_basicAuthPasswordBox.Text, "Anaf", "BasicAuth", "Password");

        _document.SetInt((int)_checkHourUpDown.Value, "Anaf", "CheckSchedule", "CheckHour");
        _document.SetInt((int)_checkMinuteUpDown.Value, "Anaf", "CheckSchedule", "CheckMinute");
        _document.SetInt((int)_daysBeforeExpirationUpDown.Value, "Anaf", "DaysBeforeExpiration");

        _document.SetString(_configFilePathBox.Text.Trim(), "Anaf", "ConfigFilePath");
        _document.SetString(_backupDirectoryBox.Text.Trim(), "Anaf", "BackupDirectory");
        _document.SetString(_initialRefreshTokenBox.Text.Trim(), "Anaf", "InitialRefreshToken");

        _document.SetString(_smtpServerBox.Text.Trim(), "Anaf", "Email", "SmtpServer");
        _document.SetInt((int)_smtpPortUpDown.Value, "Anaf", "Email", "SmtpPort");
        _document.SetString(_smtpUsernameBox.Text.Trim(), "Anaf", "Email", "Username");
        _document.SetString(_smtpPasswordBox.Text, "Anaf", "Email", "Password");
        _document.SetString(_fromAddressBox.Text.Trim(), "Anaf", "Email", "FromAddress");
        _document.SetString(_fromNameBox.Text.Trim(), "Anaf", "Email", "FromName");
        _document.SetStringArray(_toAddressesBox.Lines, "Anaf", "Email", "ToAddresses");
        _document.SetBool(_enableSslCheck.Checked, "Anaf", "Email", "EnableSsl");

        _document.SetString(_connectionStringBox.Text.Trim(), "ConnectionStrings", TokenDatabaseConnectionKey);
        _document.SetString(_apiUrlBox.Text.Trim(), "Api", "Url");

        _document.SetString(_logDefaultCombo.Text.Trim(), "Logging", "LogLevel", "Default");
        _document.SetString(_logHostingLifetimeCombo.Text.Trim(), "Logging", "LogLevel", "Microsoft.Hosting.Lifetime");
        _document.SetString(_logEfCoreCombo.Text.Trim(), "Logging", "LogLevel", "Microsoft.EntityFrameworkCore");
    }

    private void RefreshRawJsonView() => _rawJsonBox.Text = _document.ToJson();

    private void ApplyRawJsonToForm()
    {
        try
        {
            _document = AppSettingsDocument.Parse(_rawJsonBox.Text);
            ApplyDocumentToSettingsForm();
            ApplyConfiguredDatabasePath();
            SetStatus("JSON jest poprawny i został przeniesiony do formularza. Kliknij Zapisz, aby zapisać plik.");
        }
        catch (Exception ex)
        {
            ShowError("Niepoprawny JSON", ex);
        }
    }

    private static void AddSection(TableLayoutPanel stack, Control section)
    {
        var row = stack.RowStyles.Count;
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowCount = stack.RowStyles.Count;
        section.Dock = DockStyle.Fill;
        stack.Controls.Add(section, 0, row);
    }

    private static TableLayoutPanel CreateFieldTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 0
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        return table;
    }

    private static void AddField(
        TableLayoutPanel table,
        string caption,
        Control control,
        Control? trailing = null,
        int height = 26)
    {
        var row = table.RowStyles.Count;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height + 8f));
        table.RowCount = table.RowStyles.Count;

        table.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, row);

        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);

        if (trailing is null)
        {
            table.SetColumnSpan(control, 2);
            return;
        }

        trailing.Dock = DockStyle.Fill;
        table.Controls.Add(trailing, 2, row);
    }

    private static GroupBox CreateGroup(string title, Control content)
    {
        content.Dock = DockStyle.Top;

        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 8, 10, 12),
            Margin = new Padding(0, 0, 0, 12)
        };

        group.Controls.Add(content);
        return group;
    }

    private static ComboBox CreateLogLevelCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        combo.Items.AddRange(LogLevels);
        return combo;
    }

    private static void SetNumeric(NumericUpDown control, int value) =>
        control.Value = Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);

    private Button CreateBrowseFileButton(TextBox target)
    {
        var button = new Button { Text = "…", Width = 34 };

        button.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Wskaż plik",
                Filter = "Wszystkie pliki (*.*)|*.*",
                CheckFileExists = false,
                FileName = Path.GetFileName(target.Text)
            };

            var directory = SafeGetDirectory(target.Text);

            if (directory is not null)
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dialog.FileName;
            }
        };

        return button;
    }

    private Button CreateBrowseFolderButton(TextBox target)
    {
        var button = new Button { Text = "…", Width = 34 };

        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Wskaż katalog",
                UseDescriptionForTitle = true
            };

            if (Directory.Exists(target.Text))
            {
                dialog.SelectedPath = target.Text;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dialog.SelectedPath;
            }
        };

        return button;
    }
}
