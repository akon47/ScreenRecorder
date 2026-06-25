#Requires -Version 5.1
<#
.SYNOPSIS
    Regenerates the AUTO_INSTALL / AUTO_UNINSTALL blocks in Setup.nsi from the actual files in
    publish\x64 (self-contained .NET 9 + bundled FFmpeg). Root files go in SEC02; every
    subdirectory (the .NET satellite language folders) is recursed and laid out under $INSTDIR.

.PARAMETER PublishDir
    Publish output directory (default: ..\publish\x64 relative to this script).
.PARAMETER NsiFile
    NSIS script to update (default: Setup.nsi next to this script).
#>
param(
    [string]$PublishDir = (Join-Path $PSScriptRoot '..\publish\x64'),
    [string]$NsiFile    = (Join-Path $PSScriptRoot 'Setup.nsi')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PublishDir)) {
    Write-Error "PublishDir not found: $PublishDir"
    exit 1
}

# Files installed by SEC01 (with shortcuts) — excluded from the auto block.
$sec01Files = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('ScreenRecorder.exe', 'ScreenRecorder.dll', 'ScreenRecorder.Engine.dll')) {
    [void]$sec01Files.Add($name)
}

$publishRoot = (Resolve-Path -LiteralPath $PublishDir).ProviderPath.TrimEnd('\')
$rel = '..\publish\x64'

# Root files: everything except SEC01 files, debug symbols, and import libs.
$rootFiles = Get-ChildItem -Path $PublishDir -File |
    Where-Object {
        -not $sec01Files.Contains($_.Name) -and
        $_.Extension -ne '.pdb' -and
        $_.Extension -ne '.lib'
    } | Sort-Object Name

# Every subdirectory (satellite language folders), recursed.
$subFiles = [System.Collections.Generic.List[object]]::new()
foreach ($dir in (Get-ChildItem -Path $PublishDir -Directory | Sort-Object Name)) {
    foreach ($f in (Get-ChildItem -Path $dir.FullName -File -Recurse | Where-Object { $_.Extension -ne '.pdb' } | Sort-Object FullName)) {
        $subFiles.Add([pscustomobject]@{
            RelativeFile = $f.FullName.Substring($publishRoot.Length).TrimStart('\')
            RelativeDir  = $f.DirectoryName.Substring($publishRoot.Length).TrimStart('\')
        })
    }
}

# ── Build AUTO_INSTALL lines ──────────────────────────────────────────
$installLines = [System.Collections.Generic.List[string]]::new()
foreach ($f in $rootFiles) { $installLines.Add('  File "' + $rel + '\' + $f.Name + '"') }
$currentOut = '$INSTDIR'
foreach ($f in $subFiles) {
    $target = '$INSTDIR\' + $f.RelativeDir
    if ($currentOut -ne $target) {
        $installLines.Add('')
        $installLines.Add('  SetOutPath "' + $target + '"')
        $currentOut = $target
    }
    $installLines.Add('  File "' + $rel + '\' + $f.RelativeFile + '"')
}

# ── Build AUTO_UNINSTALL lines ────────────────────────────────────────
$uninstallLines = [System.Collections.Generic.List[string]]::new()
$uninstallLines.Add('  Delete "$INSTDIR\uninst.exe"')
foreach ($f in $rootFiles) { $uninstallLines.Add('  Delete "$INSTDIR\' + $f.Name + '"') }
foreach ($f in $subFiles)  { $uninstallLines.Add('  Delete "$INSTDIR\' + $f.RelativeFile + '"') }

# Remove subdirectories deepest-first.
$dirs = [System.Collections.Generic.List[string]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($f in $subFiles) { if ($seen.Add($f.RelativeDir)) { $dirs.Add($f.RelativeDir) } }
if ($dirs.Count -gt 0) {
    $maxDepth = ($dirs | ForEach-Object { $_.Split('\').Count } | Measure-Object -Maximum).Maximum
    for ($depth = $maxDepth; $depth -ge 1; $depth--) {
        foreach ($d in $dirs) {
            if ($d.Split('\').Count -eq $depth) { $uninstallLines.Add('  RMDir "$INSTDIR\' + $d + '"') }
        }
    }
}

# ── Replace between markers (UTF-8) ───────────────────────────────────
$enc = New-Object System.Text.UTF8Encoding($true)  # UTF-8 BOM (Unicode NSIS)
$nsi = [System.IO.File]::ReadAllText($NsiFile, [System.Text.Encoding]::UTF8)

function Set-MarkerContent {
    param([string]$Content, [string]$StartMarker, [string]$EndMarker, [string[]]$Lines)
    $si = $Content.IndexOf($StartMarker)
    $ei = $Content.IndexOf($EndMarker, $si + $StartMarker.Length)
    if ($si -lt 0) { throw "Start marker not found: $StartMarker" }
    if ($ei -lt 0) { throw "End marker not found: $EndMarker" }
    $afterStart = $si + $StartMarker.Length
    while ($afterStart -lt $Content.Length -and ($Content[$afterStart] -eq [char]13 -or $Content[$afterStart] -eq [char]10)) { $afterStart++ }
    $middle = if ($Lines.Count -gt 0) { ($Lines -join "`r`n") + "`r`n" } else { '' }
    return $Content.Substring(0, $afterStart) + $middle + $Content.Substring($ei)
}

$nsi = Set-MarkerContent $nsi '  ; <AUTO_INSTALL_START>'   '  ; <AUTO_INSTALL_END>'   ($installLines.ToArray())
$nsi = Set-MarkerContent $nsi '  ; <AUTO_UNINSTALL_START>' '  ; <AUTO_UNINSTALL_END>' ($uninstallLines.ToArray())

[System.IO.File]::WriteAllText($NsiFile, $nsi, $enc)
Write-Host ("Done: {0} root + {1} subdir files written to {2}." -f $rootFiles.Count, $subFiles.Count, (Split-Path $NsiFile -Leaf))
