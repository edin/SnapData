param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $PackageDirectory = "artifacts/packages",

    [string] $ToolDirectory = "artifacts/tool-smoke"
)

$ErrorActionPreference = "Stop"
$packageRoot = [System.IO.Path]::GetFullPath($PackageDirectory)
$toolRoot = [System.IO.Path]::GetFullPath($ToolDirectory)
$expectedPackages = @(
    "SnapData",
    "SnapData.Schema",
    "SnapData.Migrations",
    "SnapData.Migrations.Cli"
)

foreach ($packageId in $expectedPackages) {
    $packagePath = Join-Path $packageRoot "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Expected package was not produced: $packagePath"
    }
}

if (Test-Path -LiteralPath $toolRoot) {
    Remove-Item -LiteralPath $toolRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $toolRoot | Out-Null
$escapedPackageRoot = [System.Security.SecurityElement]::Escape($packageRoot)
$nugetConfig = Join-Path $toolRoot "NuGet.Config"
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-packages" value="$escapedPackageRoot" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8

dotnet tool install SnapData.Migrations.Cli `
    --tool-path $toolRoot `
    --version $Version `
    --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$tool = Join-Path $toolRoot "snapdata"
if ($IsWindows) {
    $tool = "$tool.exe"
}

$reportedVersion = (& $tool version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if ($reportedVersion -notmatch [regex]::Escape($Version)) {
    throw "Installed CLI reported '$reportedVersion' instead of version '$Version'."
}

$workspace = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "snapdata-package-smoke-$([Guid]::NewGuid().ToString('N'))"

function Invoke-SnapData {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

    $output = (& $tool @Arguments 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "snapdata $($Arguments -join ' ') failed:`n$output"
    }
    return $output
}

try {
    New-Item -ItemType Directory -Path $workspace | Out-Null
    Copy-Item -LiteralPath $nugetConfig -Destination $workspace
    Invoke-SnapData init --directory $workspace | Out-Null

    Push-Location $workspace
    try {
        $list = Invoke-SnapData list
        $status = Invoke-SnapData status
        $script = Invoke-SnapData script
        $migrated = Invoke-SnapData migrate --yes
        $rolledBack = Invoke-SnapData rollback --steps 2 --yes
    }
    finally {
        Pop-Location
    }

    if ($list -notmatch "2 migrations discovered" -or
        $status -notmatch "2 pending migrations" -or
        $script -notmatch "Pending migration SQL" -or
        $migrated -notmatch "2 migrations applied" -or
        $rolledBack -notmatch "2 pending migrations") {
        throw "The packaged CLI workflow did not report the expected migration states."
    }
}
finally {
    if (Test-Path -LiteralPath $workspace) {
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }
}

Write-Host "Verified all four packages and the packaged SnapData CLI $Version workflow."
