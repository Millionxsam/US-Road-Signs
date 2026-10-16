<#
.SYNOPSIS
    Builds the US Road Signs mod into dist\USRoadSigns.

.DESCRIPTION
    1. Builds the SignForge tool (renders sign faces, material textures and menu icons from data\).
    2. Runs SignForge to produce build\content.
    3. Compiles the mod DLL against the installed Cities: Skylines II assemblies.
    4. Copies everything the game needs into dist\USRoadSigns.

    -Install additionally copies dist\USRoadSigns into the game's local Mods folder.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
#>
param(
    [switch]$Install,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$content = Join-Path $root "build\content"
$dist = Join-Path $root "dist\USRoadSigns"

function Invoke-Step([string]$title, [scriptblock]$action) {
    Write-Host ""
    Write-Host "== $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)" }
}

Invoke-Step "Building SignForge" {
    dotnet build (Join-Path $root "tools\SignForge\SignForge.csproj") -c $Configuration --nologo -v q
}

Invoke-Step "Rendering sign textures and icons" {
    if (Test-Path $content) { Remove-Item $content -Recurse -Force }
    $exe = Get-ChildItem (Join-Path $root "tools\SignForge\bin\$Configuration") -Recurse -Filter SignForge.exe | Select-Object -First 1
    if (-not $exe) { throw "SignForge.exe was not found after the build" }
    & $exe.FullName build --data (Join-Path $root "data") --out $content
}

Invoke-Step "Compiling the mod" {
    dotnet build (Join-Path $root "src\USRoadSigns\USRoadSigns.csproj") -c $Configuration --nologo -v q "-p:ModContentDir=$content"
}

Write-Host ""
Write-Host "== Assembling $dist" -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
$bin = Join-Path $root "src\USRoadSigns\bin\$Configuration"
Copy-Item (Join-Path $bin "*") $dist -Recurse -Force
Get-ChildItem $dist -Filter *.pdb | Remove-Item -Force

$signCount = (Get-ChildItem (Join-Path $dist "Icons\Signs") -Filter *.png).Count
Write-Host "Mod folder ready: $dist ($signCount signs)" -ForegroundColor Green

if ($Install) {
    $mods = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\Mods"
    $target = Join-Path $mods "USRoadSigns"
    New-Item -ItemType Directory -Force $mods | Out-Null
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $dist $target -Recurse -Force
    Write-Host "Installed to $target" -ForegroundColor Green
}
