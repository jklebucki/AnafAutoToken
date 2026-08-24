<#
.SYNOPSIS
    Publikuje AnafAutoToken.Worker jako pojedynczy, samowystarczalny plik EXE.

.DESCRIPTION
    Cienka nakładka na scripts\publish-single-file.ps1 -Project Worker.
    Runtime .NET 10 wraz z ASP.NET Core jest wkompilowany w plik EXE - maszyna
    docelowa nie wymaga instalacji .NET.

    Obok pliku EXE trafiają pliki, które muszą pozostać edytowalne lub czytane
    z dysku: appsettings.json, katalog EmailTemplates oraz register_service.bat /
    unregister_service.bat.

    Dla Linuksa użyj: -Runtime linux-x64 (lub linux-arm64).
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "publish\AnafAutoToken.Worker"
)

& (Join-Path $PSScriptRoot "publish-single-file.ps1") `
    -Project Worker `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -OutputPath $OutputPath

exit $LASTEXITCODE
