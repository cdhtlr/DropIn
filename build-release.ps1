$ErrorActionPreference = 'Stop'

$solution = Join-Path $PSScriptRoot 'src\DropIn.slnx'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Solution file not found: $solution"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found. Install the .NET 8 SDK and retry.'
}

& dotnet build $solution --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

Write-Host "Release build completed: $(Join-Path $PSScriptRoot 'build\Release')"
