param(
    [Parameter(Mandatory=$true)]
    [string]$SecretsPath,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetPath
)

if (-not (Test-Path $SecretsPath)) {
    Write-Host "Secrets file not found: $SecretsPath"
    exit 0
}

if (-not (Test-Path $TargetPath)) {
    Write-Host "Target appsettings.json not found: $TargetPath"
    exit 1
}

Write-Host "Merging secrets from $SecretsPath into $TargetPath"

try {
    $secrets = Get-Content $SecretsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $appsettings = Get-Content $TargetPath -Raw -Encoding UTF8 | ConvertFrom-Json
    
    # Deep merge function
    function Merge-Objects {
        param($target, $source)
        
        $source.PSObject.Properties | ForEach-Object {
            $name = $_.Name
            $value = $_.Value
            
            if ($null -ne $value) {
                if ($value -is [PSCustomObject] -and 
                    $target.PSObject.Properties[$name] -and 
                    $target.$name -is [PSCustomObject]) {
                    Merge-Objects $target.$name $value
                } else {
                    $target | Add-Member -MemberType NoteProperty -Name $name -Value $value -Force
                }
            }
        }
    }
    
    Merge-Objects $appsettings $secrets
    
    $json = $appsettings | ConvertTo-Json -Depth 10
    Set-Content -Path $TargetPath -Value $json -Encoding UTF8
    
    Write-Host "✓ Secrets merged successfully into $TargetPath"
    exit 0
}
catch {
    Write-Host "ERROR: Failed to merge secrets: $_"
    exit 1
}
