[CmdletBinding()]
param(
    [string]$GameDir = 'F:\Steam\steamapps\common\Stonewards',
    [string]$ProfileDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ProfileDir)) {
    $ProfileDir = Join-Path $env:APPDATA 'r2modmanPlus-local\Stonewards\profiles\stonewards-auto-class-selection'
}

if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) {
    throw "Stonewards directory not found: $GameDir"
}
if (-not (Test-Path -LiteralPath $ProfileDir -PathType Container)) {
    throw "Create an r2modman profile named stonewards-auto-class-selection first: $ProfileDir"
}

$profileBep = Join-Path $ProfileDir 'BepInEx'
$bepRoot = if (Test-Path (Join-Path $profileBep 'core\BepInEx.dll')) {
    $profileBep
} elseif (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) {
    Join-Path $GameDir 'BepInEx'
} else {
    throw 'BepInEx 5 not found. Install BepInExPack in the selected r2modman profile.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is missing from PATH.'
}

$project = Join-Path $root 'StonewardsAutoClassSelection.csproj'
& dotnet build $project --configuration Release "-p:GameDir=$GameDir" "-p:BepInExDir=$bepRoot"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $LASTEXITCODE" }

$dll = Join-Path $root 'bin\Release\netstandard2.1\StonewardsAutoClassSelection.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Compiled plugin not found: $dll"
}

$targetDir = Join-Path $profileBep 'plugins\StonewardsAutoClassSelection'
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
$target = Join-Path $targetDir 'StonewardsAutoClassSelection.dll'
Copy-Item -LiteralPath $dll -Destination $target -Force
Write-Host "Installed: $target" -ForegroundColor Green
Write-Host 'Launch Stonewards from this exact r2modman profile and join/host a lobby.'
