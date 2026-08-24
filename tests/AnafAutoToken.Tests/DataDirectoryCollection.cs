namespace AnafAutoToken.Tests;

/// <summary>
/// Testy dotykajace wspolnego katalogu danych musza isc po kolei - kazdy z nich czysci
/// ten sam katalog, wiec rownolegly przebieg konczylby sie wyscigiem o pliki.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class DataDirectoryCollection
{
    public const string Name = "data-directory";
}
