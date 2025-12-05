using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;

namespace AnafAutoToken.Infrastructure.Http;

public class AnafApiClient(
    HttpClient httpClient,
    IOptions<AnafSettings> settings,
    ILogger<AnafApiClient> logger) : IAnafApiClient
{
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

            var tokenResponse = await response.Content.ReadFromJsonAsync<AnafTokenResponse>(cancellationToken);

            if (tokenResponse == null)
            {
                logger.LogError("Failed to deserialize token response");
                throw new InvalidOperationException("Failed to deserialize token response");
            }

            logger.LogInformation("Token refresh successful. Expires in: {ExpiresIn} seconds", tokenResponse.ExpiresIn);

            return tokenResponse;
        }
        catch (Exception ex) when (ex is not HttpRequestException and not InvalidOperationException)
        {
            logger.LogError(ex, "Unexpected error during token refresh");
            throw;
        }
    }
}
