namespace AnafAutoToken.Tests;

/// <summary>
/// Szablony czytamy ze źródeł, a nie z katalogu wyjściowego testów - EmailNotificationServiceTests
/// podmienia i kasuje tam pliki, więc odczyt z bin zależałby od kolejności testów.
/// </summary>
internal static class RepositoryPaths
{
    public static string EmailTemplatesDirectory { get; } = Path.Combine(
        FindRepositoryRoot(),
        "src",
        "AnafAutoToken.Core",
        "EmailTemplates");

    public static string ReadEmailTemplate(string name) =>
        File.ReadAllText(Path.Combine(EmailTemplatesDirectory, $"{name}.html"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AnafAutoToken.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Nie znaleziono katalogu repozytorium powyżej {AppContext.BaseDirectory}.");
    }
}
