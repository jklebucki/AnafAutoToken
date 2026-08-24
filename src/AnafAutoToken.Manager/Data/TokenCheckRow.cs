namespace AnafAutoToken.Manager.Data;

/// <summary>
/// Wiersz z <c>TokenCheckLogs</c> - ślad po każdym przebiegu sprawdzenia tokenu,
/// także takim, który nie wymagał odświeżenia.
/// </summary>
internal sealed record TokenCheckRow(
    int Id,
    DateTime CheckedAt,
    int Outcome,
    int Trigger,
    DateTime? AccessTokenExpiresAt,
    DateTime? RefreshTokenExpiresAt,
    string? Message)
{
    public string OutcomeText => Outcome switch
    {
        0 => "nie było potrzebne",
        1 => "ODŚWIEŻONY",
        2 => "BŁĄD",
        _ => $"nieznany ({Outcome})"
    };

    public string TriggerText => Trigger switch
    {
        0 => "harmonogram",
        1 => "ręcznie",
        2 => "start usługi",
        _ => $"nieznany ({Trigger})"
    };
}

/// <summary>Projekcja pod DataGridView.</summary>
internal sealed class TokenCheckGridRow
{
    private TokenCheckGridRow(TokenCheckRow source)
    {
        Source = source;
    }

    public TokenCheckRow Source { get; }

    public int Id => Source.Id;

    public string CheckedAt => Source.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string Outcome => Source.OutcomeText;

    public string Trigger => Source.TriggerText;

    public string AccessTokenExpiresAt => Format(Source.AccessTokenExpiresAt);

    public string RefreshTokenExpiresAt => Format(Source.RefreshTokenExpiresAt);

    public string Message => Source.Message?.ReplaceLineEndings(" ") ?? string.Empty;

    public static TokenCheckGridRow From(TokenCheckRow source) => new(source);

    private static string Format(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
}
