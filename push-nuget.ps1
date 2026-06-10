param(
    [Parameter(Mandatory)]
    [string]$ApiKey,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Resolve version from Directory.Build.props
$propsPath = Join-Path $PSScriptRoot 'Directory.Build.props'
$coreVersion = ([xml](Get-Content $propsPath)).Project.PropertyGroup.CoreVersion
$tag = "v$coreVersion"

# --- Pre-flight: git tag collision ---
$localTag = git tag -l $tag
if ($localTag) {
    Write-Error "Tag '$tag' already exists locally."
    exit 1
}
$remoteTags = git ls-remote --tags origin "refs/tags/$tag"
if ($remoteTags) {
    Write-Error "Tag '$tag' already exists on remote."
    exit 1
}

# --- Pre-flight: NuGet version collision ---
$nupkgs = Get-ChildItem -Recurse -Include *.nupkg -File |
    Where-Object { $_.FullName -notmatch '\\obj\\' }

if (-not $nupkgs) {
    Write-Error "No packages found. Run 'dotnet build -c Release' first."
    exit 1
}

foreach ($pkg in $nupkgs) {
    # Filename format: {PackageId}.{Version}.nupkg
    $pkgId = $pkg.BaseName -replace "\.$([regex]::Escape($coreVersion))$", ''
    $url = "https://api.nuget.org/v3-flatcontainer/$($pkgId.ToLower())/index.json"
    try {
        $index = Invoke-RestMethod -Uri $url -ErrorAction Stop
        if ($index.versions -contains $coreVersion) {
            Write-Error "Package '$pkgId' version '$coreVersion' already exists on nuget.org."
            exit 1
        }
    } catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        if ($_.Exception.Response.StatusCode -ne 404) { throw }
        # 404 = package not yet published at all; fine to proceed
    }
}

$packages = Get-ChildItem -Recurse -Include *.nupkg, *.snupkg -File |
    Where-Object { $_.FullName -notmatch '\\obj\\' }

# --- Tag ---
$tagCmd = "git tag -a $tag -m $tag"
if ($DryRun) {
    Write-Host "[dry-run] $tagCmd"
} else {
    Invoke-Expression $tagCmd
}

# --- Push packages ---
$pushTagCmd = "git push origin $tag"
if ($DryRun) {
    foreach ($pkg in $packages) {
        Write-Host "[dry-run] dotnet nuget push `"$($pkg.FullName)`" --api-key <ApiKey> --source https://api.nuget.org/v3/index.json --skip-duplicate"
    }
    Write-Host "[dry-run] $pushTagCmd"
    exit 0
}

foreach ($pkg in $packages) {
    dotnet nuget push $pkg.FullName --api-key $ApiKey --source https://api.nuget.org/v3/index.json --skip-duplicate
}

Invoke-Expression $pushTagCmd
