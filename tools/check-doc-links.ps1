param(
  [switch]$FailOnBroken
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
Write-Host "check-doc-links: scanning $RepoRoot"

$excludeDirs = @('bin', 'obj', 'node_modules', 'release', '.git', '.opencode')

# Sibling-checkout paths. These name PUBLIC repos (CarryOnLib, RackEmUp,
# BarrelRackDemo, CarryEntityDemo) so they are not a confidentiality problem,
# but they do not resolve from a standalone clone of this repo. Reported
# separately from genuinely broken links so the two are not confused.
$siblingPattern = '\.\./(CarryOnLib|RackEmUp|BarrelRackDemo|CarryEntityDemo)/'

# Git-ignored local files (e.g. the machine-local AGENTS.md) are not repo
# artifacts and are excluded: the scan set is what git considers part of the
# repo (tracked plus untracked-not-ignored). Falls back to scanning everything
# if git is unavailable.
$repoFileSet = $null
$gitOut = git -C $RepoRoot ls-files --cached --others --exclude-standard 2>$null
if ($LASTEXITCODE -eq 0 -and $null -ne $gitOut) {
  $repoFileSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($rel in $gitOut) {
    [void]$repoFileSet.Add([System.IO.Path]::GetFullPath((Join-Path $RepoRoot $rel)))
  }
}

$mdFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue |
  Where-Object {
    $p = $_.FullName
    foreach ($d in $excludeDirs) { if ($p -match "\\$d\\") { return $false } }
    if ($_.Name -eq 'check-doc-links.ps1') { return $false }
    if ($null -ne $repoFileSet -and -not $repoFileSet.Contains([System.IO.Path]::GetFullPath($p))) { return $false }
    $true
  }

# Build an anchor index: for each markdown file, the GitHub-style slugs of its headings.
function Get-AnchorSet([string]$path) {
  $set = @{}
  foreach ($line in (Get-Content -LiteralPath $path -ErrorAction SilentlyContinue)) {
    if ($line -match '^(#{1,6})\s+(.*?)\s*$') {
      $text = $Matches[2]
      # Strip inline markdown, then apply GitHub's slug rules.
      $text = $text -replace '\[([^\]]*)\]\([^)]*\)', '$1'
      $text = $text -replace '`', '' -replace '\*', '' -replace '_', ''
      $text = $text.ToLower().Trim()
      $text = $text -replace '[^\p{L}\p{Nd}\s-]', ''
      $text = $text -replace '\s+', '-'
      $set[$text] = $true
    }
  }
  return $set
}

$broken = @()
$sibling = @()

# [text](target) and [text](target#anchor) and bare <path>
$linkRegex = '\[[^\]]*\]\(([^)\s]+)\)'

foreach ($f in $mdFiles) {
  $rel = $f.FullName.Replace($RepoRoot, '').TrimStart([char]'\', [char]'/')
  $lineNo = 0
  foreach ($line in (Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue)) {
    $lineNo++
    foreach ($m in [regex]::Matches($line, $linkRegex)) {
      $target = $m.Groups[1].Value

      # Skip external URLs, mailto, and image-only links to binaries we don't check.
      if ($target -match '^(https?:|mailto:|#)') { continue }

      if ($target -match $siblingPattern) {
        $sibling += [pscustomobject]@{ File = $rel; Line = $lineNo; Target = $target }
        continue
      }

      # Split off the anchor.
      $anchor = $null
      $path = $target
      if ($target -match '^(.+)#(.+)$') {
        $path = $Matches[1]
        $anchor = $Matches[2]
      }
      if ([string]::IsNullOrWhiteSpace($path)) { continue }

      $resolved = Join-Path (Split-Path -Parent $f.FullName) $path
      if (-not (Test-Path -LiteralPath $resolved)) {
        $broken += [pscustomobject]@{ File = $rel; Line = $lineNo; Target = $target; Problem = 'file not found' }
        continue
      }

      if ($anchor -and $resolved -like '*.md') {
        $anchors = Get-AnchorSet $resolved
        if (-not $anchors.ContainsKey($anchor.ToLower())) {
          $broken += [pscustomobject]@{ File = $rel; Line = $lineNo; Target = $target; Problem = "anchor '#$anchor' not found" }
        }
      }
    }
  }
}

if ($sibling.Count -gt 0) {
  Write-Host ""
  Write-Host "SIBLING-CHECKOUT PATHS (public repos, do not resolve from a standalone clone)" -ForegroundColor Yellow
  $sibling | Sort-Object File, Line | Format-Table -AutoSize
}

if ($broken.Count -gt 0) {
  Write-Host ""
  Write-Host "BROKEN DOC LINKS" -ForegroundColor Red
  $broken | Sort-Object File, Line | Format-Table -AutoSize
}

Write-Host ""
Write-Host "TOTAL: $($broken.Count) broken, $($sibling.Count) sibling-checkout paths."

if ($broken.Count -gt 0) {
  if ($FailOnBroken) { exit 1 } else { exit 0 }
}
Write-Host "No broken links." -ForegroundColor Green
exit 0
