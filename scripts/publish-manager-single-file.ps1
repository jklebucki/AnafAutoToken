<#
.SYNOPSIS
    Publikuje AnafAutoToken.Manager (UI) jako pojedynczy, samowystarczalny plik EXE.

.DESCRIPTION
    Cienka nakładka na scripts\publish-single-file.ps1 -Project Manager.
    Runtime .NET 10 wraz z Windows Desktop jest wkompilowany w plik EXE - maszyna
    docelowa nie wymaga instalacji .NET.

    Zapis konfiguracji wymaga uprawnień do zapisu w katalogu instalacyjnym serwisu -
    uruchom menedżera jako Administrator, jeśli serwis stoi w Program Files.
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "publish\AnafAutoToken.Manager"
)

& (Join-Path $PSScriptRoot "publish-single-file.ps1") `
    -Project Manager `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -OutputPath $OutputPath

exit $LASTEXITCODE
