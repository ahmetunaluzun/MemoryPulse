# Prints the GitHub release notes for a version: its CHANGELOG.md section, then the download line and checksum.
# Fails when CHANGELOG.md has no section for the version, so a release cannot go out without its history.
# Usage: tools/release-notes.ps1 -Version 1.4.0 -Sha256 <hash>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Sha256
)
$ErrorActionPreference = 'Stop'

$lines = Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Encoding UTF8
$start = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l -match "^## \[$([regex]::Escape($Version))\]" })
if ($start -lt 0) { throw "CHANGELOG.md has no section for $Version" }
$end = [Array]::FindIndex($lines, $start + 1, [Predicate[string]] { param($l) $l -match '^## ' })
if ($end -lt 0) { $end = $lines.Count }
$section = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
if (-not $section) { throw "CHANGELOG.md section for $Version is empty" }

"## What's new`n`n$section`n`n## Download`n`nDownload ``MemoryPulse.exe`` and run it. No installation needed (Windows 10/11).`n`nSHA-256: ``$Sha256``"
