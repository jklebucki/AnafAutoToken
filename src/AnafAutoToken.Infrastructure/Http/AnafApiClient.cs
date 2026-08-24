using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace AnafAutoToken.Infrastructure.Http;

public class AnafApiClient(
    HttpClient httpClient,
    IOptions<AnafSettings> settings,
    ILogger<AnafApiClient> logger) : IAnafApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AnafSettings _settings = settings.Value;

    public async Task<AnafTokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            };

            var content = new FormUrlEncodedContent(formData);

            // Add Basic Auth header
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.BasicAuth.Username}:{_settings.BasicAuth.Password}"));
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            logger.LogInformation("Sending token refresh request to ANAF API");

            var response = await httpClient.PostAsync(_settings.TokenEndpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Token refresh failed. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException(
                    $"Token refresh failed with status {response.StatusCode}: {errorContent}");
            }

            // Czytamy body jako tekst, a nie prosto do modelu - surowa odpowiedź jest
            // potem archiwizowana na dysku jako zabezpieczenie na wypadek problemów
            // z zapisem do bazy albo do config.ini.
            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

            AnafTokenResponse? tokenResponse;

            try
            {
                tokenResponse = JsonSerializer.Deserialize<AnafTokenResponse>(rawJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to deserialize token response");
                throw new InvalidOperationException("Failed to deserialize token response", ex);
            }

            if (tokenResponse == null)
            {
                logger.LogError("Failed to deserialize token response");
                throw new InvalidOperationException("Failed to deserialize token response");
            }

            tokenResponse.RawJson = rawJson;

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                logger.LogError("ANAF returned a token response without an access token");
                throw new InvalidOperationException("ANAF returned a token response without an access token");
            }

            if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            {
                logger.LogWarning(
                    "ANAF token response did not contain a refresh token. The previously stored refresh token will be kept");
            }

            logger.LogInformation(
                "Token refresh successful. Expires in: {ExpiresIn} seconds, refresh token returned: {HasRefreshToken}",
                tokenResponse.ExpiresIn,
                !string.IsNullOrWhiteSpace(tokenResponse.RefreshToken));

            return tokenResponse;
        }
        catch (Exception ex) when (ex is not HttpRequestException and not InvalidOperationException)
        {
            logger.LogError(ex, "Unexpected error during token refresh");
            throw;
        }
    }
}
