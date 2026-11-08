param(
    [string]$Configuration = 'Release',
    [string[]]$RuntimeIdentifiers = @('win-x64', 'win-x86', 'win-arm64')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$outRoot = Join-Path $root 'publish'
$installerScript = Join-Path $root 'installer\DropIn.iss'
$projects = @(
    (Join-Path $root 'src\DropIn.Installer\DropIn.Installer.csproj'),
    (Join-Path $root 'src\DropIn.Packager\DropIn.Packager.csproj')
)
$iconProject = Join-Path $root 'src\DropIn.IconHandler\DropIn.IconHandler.csproj'
$machines = @{ 'win-x64' = 0x8664; 'win-x86' = 0x14c; 'win-arm64' = 0xaa64 }

function Assert-PeMachine([string]$Path, [int]$Expected) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne $Expected) {
            throw "Incorrect executable architecture: $Path"
        }
    }
    finally { $stream.Dispose() }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found. Install the .NET 8 SDK or newer.'
}
$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$iscc = if ($isccCommand) { $isccCommand.Source } else { $env:INNO_SETUP_COMPILER }
if (-not $iscc -or -not (Test-Path -LiteralPath $iscc -PathType Leaf)) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Inno Setup 6 is required. Install it or set INNO_SETUP_COMPILER to ISCC.exe.'
}

$installerDir = Join-Path $outRoot 'installers'
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
foreach ($rid in $RuntimeIdentifiers) {
    if (-not $machines.ContainsKey($rid)) {
        throw "Unsupported runtime identifier: $rid"
    }
    $dir = Join-Path $outRoot $rid
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
    foreach ($project in $projects) {
        & dotnet publish $project --configuration $Configuration --runtime $rid --self-contained false --output $dir -p:SatelliteResourceLanguages=en
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project ($rid): $LASTEXITCODE" }
    }
    # The reference build may have copied a host for the build machine into this directory.
    $comHost = Join-Path $dir 'DropIn.IconHandler.comhost.dll'
    if (Test-Path -LiteralPath $comHost -PathType Leaf) { Remove-Item -LiteralPath $comHost }
    & dotnet publish $iconProject --configuration $Configuration --runtime $rid --self-contained false --output $dir -p:SatelliteResourceLanguages=en
    if ($LASTEXITCODE -ne 0) { throw "Icon handler publish failed for ${rid}: $LASTEXITCODE" }
    foreach ($name in @('DropIn.Installer.exe', 'DropIn.Packager.exe', 'DropIn.IconHandler.comhost.dll')) {
        Assert-PeMachine (Join-Path $dir $name) $machines[$rid]
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dir 'DropIn.IconHandler.runtimeconfig.json') -PathType Leaf)) {
        throw "Icon handler runtime configuration is missing for $rid"
    }
    & $iscc "/DSourceDir=$dir" "/DOutputDir=$installerDir" "/DTargetRid=$rid" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed for ${rid}: $LASTEXITCODE" }
}
Write-Host "Published applications: $outRoot"
Write-Host "Installers: $installerDir"
