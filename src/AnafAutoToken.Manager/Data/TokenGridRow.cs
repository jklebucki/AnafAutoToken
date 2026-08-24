namespace AnafAutoToken.Manager.Data;

/// <summary>
/// Display projection of a <see cref="TokenLogRow"/>. Tokens are shortened here so the grid
/// stays readable; the full values live in <see cref="Source"/> and in the detail boxes.
/// </summary>
internal sealed class TokenGridRow
{
    private TokenGridRow(TokenLogRow source)
    {
        Source = source;
    }

    public TokenLogRow Source { get; }

    public int Id => Source.Id;

    public string SavedAt => Format(Source.CreatedAt);

    public string Status => Source.Status;

    public string AccessTokenExpiresAt => Format(Source.AccessTokenExpiresAt ?? Source.ExpiresAt);

    public string RefreshTokenExpiresAt => Format(Source.RefreshTokenExpiresAt);

    public string AccessTokenPreview => Preview(Source.AccessToken);

    public string RefreshTokenPreview => Preview(Source.RefreshToken);

    public string ResponseStatusCode => Source.ResponseStatusCode?.ToString() ?? "-";

    public string ErrorMessage => Source.ErrorMessage?.ReplaceLineEndings(" ") ?? string.Empty;

    public static TokenGridRow From(TokenLogRow source) => new(source);

    private static string Format(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    private static string Preview(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "(brak)";
        }

        return token.Length <= 24 ? token : $"{token[..12]}…{token[^8..]} ({token.Length} znaków)";
    }
}
