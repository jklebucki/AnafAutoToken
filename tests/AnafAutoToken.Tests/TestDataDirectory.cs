using System.Runtime.CompilerServices;
using AnafAutoToken.Shared.Configuration;

namespace AnafAutoToken.Tests;

/// <summary>
/// <see cref="AppPaths"/> wylicza katalog danych raz na proces, więc podmiana musi nastąpić
/// zanim jakikolwiek test go dotknie. Inicjalizator modułu jest jedynym miejscem, które daje
/// taką gwarancję niezależnie od kolejności testów.
/// </summary>
internal static class TestDataDirectory
{
    public static string Path { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AnafAutoTokenTests_{Guid.NewGuid():N}");

        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryEnvironmentVariable, Path);
    }
}
