<#
.SYNOPSIS
    Publikuje AnafAutoToken.Exporter jako pojedynczy, samowystarczalny plik EXE.

.DESCRIPTION
    Cienka nakładka na scripts\publish-single-file.ps1 -Project Exporter.
    Runtime .NET 10 jest wkompilowany w plik EXE - maszyna docelowa nie wymaga
    instalacji .NET.
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = "publish\AnafAutoToken.Exporter"
)

& (Join-Path $PSScriptRoot "publish-single-file.ps1") `
    -Project Exporter `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -OutputPath $OutputPath

exit $LASTEXITCODE
