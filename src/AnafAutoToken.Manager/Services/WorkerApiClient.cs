using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnafAutoToken.Shared.Models;

namespace AnafAutoToken.Manager.Services;

/// <summary>
/// Rozmawia z API działającego workera. Ręczne odświeżenie celowo idzie przez serwis,
/// a nie przez kod uruchomiony w menedżerze - inaczej dwa procesy pisałyby jednocześnie
/// do config.ini i do bazy, a ANAF dostałby dwa żądania z tym samym refresh tokenem.
/// </summary>
internal static class WorkerApiClient
{
    public const string DefaultBaseUrl = "http://127.0.0.1:5099";

    // Odświeżenie to wywołanie ANAF z politykami retry plus wysyłka maili - potrafi potrwać.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<ManualTokenRefreshResponse> TriggerRefreshAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildUri(baseUrl, "api/tokens/refresh");

        HttpResponseMessage response;

        try
        {
            response = await Http.PostAsync(requestUri, content: null, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Worker nie odpowiada pod adresem {requestUri}. Sprawdź, czy serwis działa "
                + $"(zakładka „Serwis systemowy”) i czy Api:Url wskazuje właściwy port.{Environment.NewLine}{ex.Message}",
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Worker nie odpowiedział w ciągu 3 minut. Sprawdź logi serwisu.",
                ex);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                "Odświeżanie tokenu już trwa (zaplanowany przebieg albo inne ręczne wywołanie). Spróbuj ponownie za chwilę.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new InvalidOperationException(
                $"Worker odpowiedział kodem {(int)response.StatusCode} ({response.StatusCode})."
                + (string.IsNullOrWhiteSpace(body) ? string.Empty : $"{Environment.NewLine}{body.Trim()}"));
        }

        return await response.Content.ReadFromJsonAsync<ManualTokenRefreshResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Worker zwrócił pustą odpowiedź.");
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Adres API workera nie jest poprawnym URL: {trimmed}");
        }

        // 0.0.0.0 jest adresem nasłuchu, nie adresem docelowym - kierujemy na pętlę zwrotną.
        if (baseUri.Host is "0.0.0.0" or "[::]" or "*" or "+")
        {
            baseUri = new UriBuilder(baseUri) { Host = "127.0.0.1" }.Uri;
        }

        return new Uri(baseUri, relativePath);
    }
}
