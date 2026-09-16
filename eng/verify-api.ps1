param(
    [Parameter(Mandatory = $true)][string] $Version,
    [Parameter(Mandatory = $true)][string] $PackageDirectory
)
$ErrorActionPreference = "Stop"
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$smokeProject = Join-Path $PSScriptRoot "package-api-smoke/PackageApiSmoke.csproj"
$smokeConfig = Join-Path $PSScriptRoot "package-api-smoke/NuGet.Config"
$cacheRoot = Join-Path ([System.IO.Path]::GetTempPath()) "snapdata-api-smoke-$([Guid]::NewGuid().ToString('N'))"
foreach ($id in @("SnapData", "SnapData.Schema", "SnapData.Migrations")) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "$id.$Version.nupkg"))) {
        throw "Missing local package: $id.$Version.nupkg"
    }
}
$previousFeed = $env:SNAPDATA_SMOKE_FEED
try {
    $env:SNAPDATA_SMOKE_FEED = $packageRoot
    # Fresh cache plus source mapping prevents a published package with the same
    # version from accidentally standing in for this working-tree build.
    dotnet restore $smokeProject --configfile $smokeConfig --packages $cacheRoot "-p:SnapDataPackageVersion=$Version" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Package API consumer restore failed." }
    foreach ($framework in @("net8.0", "net10.0")) {
        dotnet run --project $smokeProject --framework $framework --configuration Release --no-restore "-p:SnapDataPackageVersion=$Version" --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "Package API consumer failed on $framework." }
    }
}
finally { $env:SNAPDATA_SMOKE_FEED = $previousFeed }
Write-Host "Verified package API consumers. Isolated package cache retained at $cacheRoot"
