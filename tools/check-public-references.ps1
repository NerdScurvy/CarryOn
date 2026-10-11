param(
  [switch]$FailOnLeak
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
Write-Host "check-public-references: scanning $RepoRoot"

$excludeDirs = @(
  'bin', 'obj', 'node_modules', 'release', '.git', '.opencode'
)

$extensions = @(
  '*.cs', '*.csproj', '*.md', '*.json', '*.xml', '*.txt', '*.props', '*.targets',
  '*.yml', '*.yaml', '*.js', '*.sln', '*.slnx', '*.config', '*.ps1', '*.bat', '*.cmd'
)

$leaks = @()

# Patterns that must never appear in public repos (CarryOn/CarryOnLib)
# 1. Explicit CarryOnWorking references
$patterns = @(
  @{ Name = 'CarryOnWorking'; Regex = '\bCarryOnWorking\b' },
  @{ Name = 'plans/active';   Regex = 'plans/(active|backlog|completed|deferred)/' },
  @{ Name = 'codereview/';    Regex = '\bcodereview/' },
  @{ Name = 'ModDB-cmt';      Regex = 'ModDB\s+cmt-' },
  @{ Name = 'moddb-report';   Regex = 'the\s+moddb\s+report' },
  @{ Name = 'cmt-236131';     Regex = 'cmt-236131' },
  @{ Name = 'issue-2026-09-20'; Regex = 'issue\s+2026-09-20' },
  @{ Name = 'defect-2-overlay'; Regex = "overlay\s+merge-loss\s+issue's\s+defect\s+2" },
  # Internal issue filenames cited in public source/docs. These are dated slugs from
  # CarryOnWorking/issues/ and plans/ - none of them exist in this repo.
  @{ Name = 'issue-slug';     Regex = '\b20\d\d-\d\d-\d\d-(overlay-carryable-merge-lost-on-recondition|recondition-not-a-reset-non-reversible-config-edits|carryables-chest-toggle-ineffective-default-automapper|creative-detach-recovery-nre|config-watchdog-overwrites-user-edits-with-defaults|trunk-dummy-pickup-vanilla-straddle-divergence|carried-block-claim-overlap-footprint-attached-signs)\b' },
  # Internal plan slugs cited by bare name (no directory prefix) in src/ and docs/.
  @{ Name = 'plan-slug';      Regex = '\b20\d\d-\d\d-\d\d-(conditioning-baseline-convergence|carried-behavior-sync-client-staleness|carryables-on-back-toggle-slot-restore|attachable-render-corruption-recovery|ground-storage-bag-open-pickup-guard|atlas-tests-mr-review|atlas-mr-medium-findings|block-interaction-reach-validation|config-watchdog-automated-test-scenarios|dropped-trunk-hole-placement-scenarios|place-down-failure-phantom-reconciliation)\b' },
  # ModDB is Vintage Story's PUBLIC mod-distribution site, and the repo legitimately
  # links to it (README "[ModDB][DL]", "Download Carry On from ModDB"). Only the
  # private-artifact *phrasings* around it are leaks - a bare "ModDB" never is.
  # "ModDB cmt-<n>" and "the moddb report" are matched by the two rules above; these
  # are the remaining phrasings the audit found in src/.
  @{ Name = 'moddb-artifact'; Regex = '(?i)\bmod\s*db\s+(report|data-loss|data-clobber|reproducer|issue)\b' },
  @{ Name = 'known-issue-hash'; Regex = 'known\s+issue\s+#\d' },
  # Review shorthand: "review M8", "codereview M2", "review 2026-09-24"
  @{ Name = 'review-shorthand'; Regex = '\breview\s+[MHLCQR]\d\b|\breview\s+20\d\d-\d\d-\d\d\b' },
  # Dated bare plan reference: "plan 2026-09-06", "see plan 2026-08-02"
  @{ Name = 'bare-plan-date';  Regex = '\bplan\s+20\d\d-\d\d-\d\d\b' },
  # The conditioning plan's own phase numbering used as a citation
  @{ Name = 'plan-phase';     Regex = "\bplan's\s+(D\d|closed question\s+\d|spike\s+\d)" },
  # Review-finding citations leaked into public comments: a review's own finding
  # number ("code-review finding 45") and an unmerged PR's test-plan sections
  # ("pr77 test plan section 7"). Both are private-artifact phrasings.
  @{ Name = 'code-review-finding'; Regex = '\bcode[- ]review\s+finding\s+\d+\b' },
  @{ Name = 'pr-test-plan';         Regex = '\bpr\d+\s+test\s+plan(?:\s+section\s+\d+)?\b' }
)

# The scan set is what git considers part of the repo: tracked files plus
# untracked files the ignore rules would admit. Git-IGNORED local files (e.g.
# the machine-local AGENTS.md) are not repo artifacts and are excluded - the
# gate exists to keep what gets committed clean. Falls back to scanning
# everything if git is unavailable (e.g. a repo copied without .git).
$repoFileSet = $null
$gitOut = git -C $RepoRoot ls-files --cached --others --exclude-standard 2>$null
if ($LASTEXITCODE -eq 0 -and $null -ne $gitOut) {
  $repoFileSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($rel in $gitOut) {
    [void]$repoFileSet.Add([System.IO.Path]::GetFullPath((Join-Path $RepoRoot $rel)))
  }
}

$files = Get-ChildItem -Path $RepoRoot -Recurse -File -Include $extensions -ErrorAction SilentlyContinue |
  Where-Object {
    $p = $_.FullName
    # Exclude build artifacts
    foreach ($d in $excludeDirs) { if ($p -match "\\$d\\") { return $false } }
    # Exclude this script - it necessarily contains the patterns it searches for
    if ($_.Name -eq 'check-public-references.ps1') { return $false }
    # Exclude git-ignored local files (not repo artifacts)
    if ($null -ne $repoFileSet -and -not $repoFileSet.Contains([System.IO.Path]::GetFullPath($p))) { return $false }
    $true
  }

foreach ($f in $files) {
  $content = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue
  if (-not $content) { continue }
  # Collapse leading comment decorations (//, ///, /*, *, <!--, #, ...) to a
  # single space so a phrasal leak split across a wrapped comment line still
  # matches its pattern. Non-comment text is untouched.
  $scanContent = [regex]::Replace($content, '(?m)^[ \t]*(?:///+|/\*+|\*+/|\*+|<!--|-->|#)[ \t]?', ' ')
  foreach ($pat in $patterns) {
    $m = [regex]::Matches($scanContent, $pat.Regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($m.Count -gt 0) {
      $leaks += [pscustomobject]@{
        File    = $f.FullName.Replace($RepoRoot, '').TrimStart([char]'\', [char]'/')
        Pattern = $pat.Name
        Count   = $m.Count
      }
    }
  }
}

# ---------------------------------------------------------------------------
# Pass 2: internal shorthand IDs.
#
# These are auto-generated review/plan finding IDs. They are NOT a global
# namespace - the same token means different things in different review
# documents - so none of them may be published, and none may be renamed to a
# new public ID. The design is the public artifact; the numbering is not.
#
# Two passes with different strictness, because the two trees have different
# false-positive profiles:
#
#   2a  src/   Strict. Every M/H/C/I/L/Q/D/B token in a comment line is a leak.
#              No false positives here: the HUD anchor names (L1/L2/L3, R1-R3)
#              live in src/Client/ but only inside string literals, never in a
#              comment, and the only src/ use of L1 is the literal phrase
#              "review finding L1".
#
#   2b  tests/ Contextual. Most token families here are *test row labels* that
#              the public tests/CarryOn.Scenarios/README.md defines in its own
#              inventory tables, so they resolve in-repo and are left alone
#              (R*, W*, N*, D*, A*, E*, S*, P*, G*). Flag a token only when the
#              same line also carries review/plan context, which is how the
#              genuinely private references (M*, C*, I*, Q*, and the review
#              sense of H*) are always written.
#
#   2c  docs/  Strict on prose, silent on code fences. Mermaid node ids (q0, q1,
#              B2, B3) and shell/config examples live inside fences and are
#              skipped wholesale. The one prose allow-list is the HUD anchor set
#              in carryon-chat-commands.md, which is a public command argument,
#              not a citation - see $mdAllow below.
# ---------------------------------------------------------------------------

$idTokens = 'M[1-9]|H[1-4]|C[1-7]|I[1-4]|L[1-5]|Q[1-5]|D[1-4]|B[1-4]'
# The lookahead on `plan` excludes the render domain terms "the plan cache" and
# "plan-cache key", which are real code identifiers (TransformTemplateManager /
# PackAdjustmentHandler), not citations.
$reviewCtx = 'codereview|review\s|finding\s|\bplan\b(?!\s*-?\s*cache)|Phase\s+\d'

# Files where a token in the deny-list is a genuine public identifier. Kept as an
# explicit list with a reason each, so an addition is a reviewed decision rather
# than a regex loosening.
$mdAllow = @{
  'docs\carryon-chat-commands.md' = 'HUD anchor names (L1-L3, R1-R3) are public /carryon gui set arguments'
}

$idLeakLines = @()

foreach ($f in ($files | Where-Object { $_.Extension -eq '.cs' -or $_.Extension -eq '.md' })) {
  $rel = $f.FullName.Replace($RepoRoot, '').TrimStart([char]'\', [char]'/')
  $isSrc = $rel -match '^src[\\/]'
  $isMd  = $f.Extension -eq '.md'
  $allowed = $isMd -and $mdAllow.ContainsKey($rel)
  $lineNo = 0
  $inFence = $false
  foreach ($line in (Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue)) {
    $lineNo++

    if ($isMd) {
      if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
      if ($inFence -or $allowed) { continue }
    } else {
      $trimmed = $line.TrimStart()
      if (-not ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*'))) { continue }
    }

    $m = [regex]::Matches($line, '(?<![\w`"-])(' + $idTokens + ')(?![\w`"-])')
    if ($m.Count -eq 0) { continue }
    if ((-not $isSrc -and -not $isMd) -and ($line -notmatch $reviewCtx)) { continue }
    $idLeakLines += [pscustomobject]@{
      File = $rel
      Line = $lineNo
      IDs   = (($m | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ',')
      Tree  = $(if ($isSrc) { 'src' } elseif ($isMd) { 'docs' } else { 'tests' })
    }
  }
}

if ($leaks.Count -gt 0) {
  Write-Host ""
  Write-Host "PATH / ARTIFACT LEAKS DETECTED" -ForegroundColor Red
  $leaks | Sort-Object File, Pattern | Format-Table -AutoSize
}

if ($idLeakLines.Count -gt 0) {
  Write-Host ""
  Write-Host "INTERNAL SHORTHAND IDs IN COMMENTS/STRINGS DETECTED" -ForegroundColor Red
  $idLeakLines | Format-Table -AutoSize
}

$total = ($leaks | Measure-Object -Property Count -Sum).Sum + $idLeakLines.Count
Write-Host ""
Write-Host "TOTAL: $($leaks.Count) path/artifact sites, $($idLeakLines.Count) shorthand-ID lines ($total checks)."

if (($leaks.Count -gt 0) -or ($idLeakLines.Count -gt 0)) {
  if ($FailOnLeak) { exit 1 } else { exit 0 }
} else {
  Write-Host "Clean." -ForegroundColor Green
  exit 0
}
