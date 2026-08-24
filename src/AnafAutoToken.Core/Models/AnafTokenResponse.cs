using System.Text.Json.Serialization;

namespace AnafAutoToken.Core.Models;

public class AnafTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    // ANAF rotates the refresh token on every refresh, but the field is treated as
    // optional so that a response carrying only a new access token still deserializes
    // instead of failing the whole refresh. The caller keeps the previous refresh
    // token when this is null or blank.
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("refresh_token_expires_in")]
    public int? RefreshTokenExpiresIn { get; init; }

    /// <summary>
    /// Surowe body odpowiedzi ANAF, dokładnie tak jak przyszło. Trzymamy je, żeby móc
    /// zarchiwizować pełną odpowiedź na dysku - także pola, których nie modelujemy.
    /// Nie jest częścią kontraktu JSON, stąd JsonIgnore.
    /// </summary>
    [JsonIgnore]
    public string RawJson { get; set; } = string.Empty;
}
