#Requires -Version 7.0

<#
.SYNOPSIS
Reads the version of the NuGet packages in a directory from their .nuspec files, and checks they are
all at one version (and, when -Tag is given, that it matches the release tag).

.DESCRIPTION
Used by the GitHub workflows. Writes these step outputs:
  version   the packages' version
  tag       the release tag: v<version>

.EXAMPLE
./.github/scripts/GetPackageVersion.ps1 -Directory package -Tag v1.0.1
#>
[CmdletBinding()]
param(
    # The directory with the .nupkg files
    [Parameter(Mandatory)] [string] $Directory,

    # When given, the release tag the packages' version must match
    [string] $Tag
)

$ErrorActionPreference = 'Stop'

function Read-Nuspec([string] $Path) {
    $package = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        # The .nuspec sits at the root of the package
        $nuspecs = @($package.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' })
        if ($nuspecs.Count -ne 1) {
            Write-Error "${Path}: expected one .nuspec at the root, found $($nuspecs.Count)"
        }
        $reader = [System.IO.StreamReader]::new($nuspecs[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally {
        $package.Dispose()
    }

    # Property access on XML ignores the namespace, which depends on the nuspec schema version
    $id = "$($nuspec.package.metadata.id)".Trim()
    $version = "$($nuspec.package.metadata.version)".Trim()
    if (-not $id -or -not $version) {
        Write-Error "${Path}: the .nuspec has no id or version"
    }
    [pscustomobject]@{ Id = $id; Version = $version; Path = $Path }
}

$found = @{}
$versions = @()
foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter '*.nupkg' -File | Sort-Object Name) {
    $package = Read-Nuspec $file.FullName
    Write-Host "$($file.Name): $($package.Id) $($package.Version)"
    if ($found.ContainsKey($package.Id)) {
        Write-Error "More than one $($package.Id) package: $($found[$package.Id].Path) and $($package.Path)"
    }
    $found[$package.Id] = $package
    $versions += $package.Version
}

$versions = @($versions | Select-Object -Unique)
if ($versions.Count -eq 0) {
    Write-Error "No packages in $Directory"
}
if ($versions.Count -ne 1) {
    Write-Error "The packages have different versions: $($versions -join ', ')"
}

$version = $versions[0]
if ($Tag -and "v$version" -ne $Tag) {
    Write-Error "The packages are v$version but the release is tagged $Tag"
}

Write-Host "Package version: $version"

Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value @(
    "version=$version"
    "tag=v$version"
)
