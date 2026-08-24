namespace AnafAutoToken.Core.Services;

/// <summary>
/// Zapisuje surową odpowiedź ANAF po udanym odświeżeniu tokenu.
/// </summary>
public interface IRefreshResponseArchive
{
    /// <summary>
    /// Zapisuje odpowiedź do pliku i zwraca jego pełną ścieżkę, albo <c>null</c>,
    /// jeśli nie było czego zapisać.
    /// </summary>
    Task<string?> SaveAsync(
        string? rawResponse,
        DateTime refreshedAt,
        CancellationToken cancellationToken = default);
}
