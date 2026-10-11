[CmdletBinding()]
param(
    [ValidateSet("All", "CarryOn.Scenarios", "CarryOn.Scenarios.ExampleMods")]
    [string]$Suite = "All",
    [switch]$Verify
)

# Generates the mechanical test-coverage index (INVENTORY.md) for the Atlas
# scenario suites. Deterministic: same source -> byte-identical file (LF, no
# BOM). -Verify exits 1 when a committed inventory disagrees with a fresh
# generation, or when annotation coverage is incomplete (a class missing
# area/summary, a row missing summary - the Phase-4 gate; plain generation
# keeps the report-only tally in the emitted file).
#
# Parsing model (Tier 1 - deliberately lexical, no Roslyn):
#   - strip // and /* */ comments, string literals (regular, verbatim and
#     interpolated), raw string literals (""" runs, incl. $/$$ prefixes) and
#     char literals, so braces/brackets in prose and JSON templates cannot
#     corrupt the brace-depth tracking
#   - track brace depth per file to scope a class body; attributes seen before
#     a class declaration attach to that class ([AtlasDataFiles] fixtures),
#     attributes inside a class attach to the next method
#   - a row is a public method carrying [AtlasScenario], [Fact] or [Theory]
#     ([Theory] counts one row per [InlineData], minimum 1)
#   - known limitation: attributes with arguments spanning multiple lines are
#     not matched (none exist in these suites today - no [Theory] rows)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

$Projects = [ordered]@{
    "CarryOn.Scenarios"             = "tests/CarryOn.Scenarios"
    "CarryOn.Scenarios.ExampleMods" = "tests/CarryOn.Scenarios.ExampleMods"
}

$AtlasAttr     = "AtlasScenario"
$FactAttr      = "Fact"
$TheoryAttr    = "Theory"
$InlineAttr    = "InlineData"
$DataFilesAttr = "AtlasDataFiles"
$TraitAttr     = "Trait"

$ReAttr      = '\[\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(([^\]]*)\))?\]'
$ReClass     = '^\s*(?:public\s+|internal\s+)?(?:abstract\s+|sealed\s+|static\s+|partial\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)'
$ReMethod    = '^\s*public\s+(?:async\s+|static\s+|override\s+)*\w[\w<\[>.?,]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\('
$ReNonMember = '^\s*public\s+(?:sealed\s+|static\s+|partial\s+)*\b(record|interface|struct|enum|delegate)\b'

function ConvertTo-CodeLine {
    param([string]$Line, [hashtable]$State)

    $sb = New-Object System.Text.StringBuilder
    $i = 0
    $len = $Line.Length
    $last = [char]0
    $prev = [char]0

    while ($i -lt $len) {
        $c = $Line[$i]

        if ($State.InBlock) {
            if ($c -eq '*' -and ($i + 1) -lt $len -and $Line[$i + 1] -eq '/') {
                $State.InBlock = $false
                $i += 2
                continue
            }
            $i++
            continue
        }

        if ($State.InRaw -gt 0) {
            if ($c -eq '"') {
                $j = $i
                while ($j -lt $len -and $Line[$j] -eq '"') { $j++ }
                if (($j - $i) -ge $State.InRaw) { $State.InRaw = 0 }
                $i = $j
                continue
            }
            $i++
            continue
        }

        if ($State.InString) {
            if ($State.Verbatim) {
                if ($c -eq '"') {
                    if (($i + 1) -lt $len -and $Line[$i + 1] -eq '"') {
                        $i += 2
                        continue
                    }
                    $State.InString = $false
                }
                $i++
                continue
            }
            if ($c -eq '\' -and ($i + 1) -lt $len) {
                $i += 2
                continue
            }
            if ($c -eq '"') { $State.InString = $false }
            $i++
            continue
        }

        # code level
        if ($c -eq '/' -and ($i + 1) -lt $len) {
            if ($Line[$i + 1] -eq '/') { break }
            if ($Line[$i + 1] -eq '*') {
                $State.InBlock = $true
                $i += 2
                continue
            }
        }

        if ($c -eq "'") {
            $i++
            while ($i -lt $len) {
                if ($Line[$i] -eq '\') { $i += 2; continue }
                if ($Line[$i] -eq "'") { $i++; break }
                $i++
            }
            continue
        }

        if ($c -eq '"') {
            $j = $i
            while ($j -lt $len -and $Line[$j] -eq '"') { $j++ }
            $run = $j - $i
            if ($run -ge 3) {
                $State.InRaw = $run
                $i = $j
                continue
            }
            if ($run -eq 2) { $i = $j; continue }   # empty string literal ""
            $verbatim = ($last -eq '@') -or (($last -eq '$') -and ($prev -eq '@'))
            $State.InString = $true
            $State.Verbatim = $verbatim
            $i++
            continue
        }

        [void]$sb.Append($c)
        $prev = $last
        $last = $c
        $i++
    }
    return $sb.ToString()
}

function Get-RawAttributeArgs {
    param([string]$RawLine, [string]$AttrName)
    # Capture up to the attribute's own closing ")]" - greedy to the LAST ")" that
    # closes with "]" - so a string argument containing "]" (e.g. "Slots[Back]")
    # is not truncated. Attributes sit alone on their physical line in this
    # codebase, so the attribute closer is always the line's final "]".
    $rm = [regex]::Match($RawLine, '\[\s*' + [regex]::Escape($AttrName) + '\s*(?:\((.*)\)\s*\])?')
    if ($rm.Success -and $rm.Groups[1].Success) { return $rm.Groups[1].Value }
    return ''
}

# Parses a [Trait("key", "value")] argument list into {Key,Value}. Values may
# contain commas and punctuation (that is the point); embedded double quotes are
# escaped as \" and backslashes as \\. One [Trait] per physical line - an
# attribute whose arguments wrap across lines is not seen at all (same known
# limitation as every other attribute in this lexical parser).
function Parse-TraitArgs {
    param([string]$RawArgs)
    if (-not $RawArgs) { return $null }
    $vals = New-Object System.Collections.ArrayList
    foreach ($m in [regex]::Matches($RawArgs, '"((?:[^"\\]|\\.)*)"')) {
        [void]$vals.Add($m.Groups[1].Value.Replace('\"', '"').Replace('\\', '\'))
    }
    if ($vals.Count -lt 2) { return $null }
    return [pscustomobject]@{ Key = $vals[0]; Value = $vals[1] }
}

function Get-SuiteInventory {
    param([string]$ProjectName)

    $projectDir = Join-Path $RepoRoot $Projects[$ProjectName]

    $fileList = New-Object 'System.Collections.Generic.List[string]'
    Get-ChildItem -LiteralPath $projectDir -Recurse -Filter '*.cs' | ForEach-Object {
        if ($_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -ne 'AssemblyInfo.cs') {
            $fileList.Add($_.FullName)
        }
    }
    # Ordinal (byte-wise) sort: culture-aware sorting would make the emitted
    # class order machine/locale-dependent.
    $fileNames = [string[]]$fileList.ToArray()
    [System.Array]::Sort($fileNames, [System.StringComparer]::Ordinal)

    $classes = New-Object System.Collections.ArrayList
    $pendingClassAttrs = New-Object System.Collections.ArrayList

    foreach ($file in $fileNames) {
        $fileRel = $file.Substring($RepoRoot.Length + 1).Replace('\', '/')
        $state = @{ InBlock = $false; InString = $false; Verbatim = $false; InRaw = 0 }
        $current = $null
        $declDepth = -1
        $depth = 0
        $pending = New-Object System.Collections.ArrayList

        foreach ($rawLine in [System.IO.File]::ReadLines($file)) {
            $code = ConvertTo-CodeLine -Line $rawLine -State $state
            $openCount = ([regex]::Matches($code, '\{')).Count
            $closeCount = ([regex]::Matches($code, '\}')).Count
            $justDeclared = $false

            $cm = [regex]::Match($code, $ReClass)
            if ($cm.Success) {
                $cls = [pscustomobject]@{
                    Name     = $cm.Groups[1].Value
                    File     = $fileRel
                    Fixtures = New-Object System.Collections.ArrayList
                    Methods  = New-Object System.Collections.ArrayList
                    Traits   = @{}
                }
                foreach ($pa in $pendingClassAttrs) {
                    if ($pa.Name -eq $TraitAttr) {
                        $t = Parse-TraitArgs -RawArgs $pa.RawArgs
                        if ($null -ne $t) { $cls.Traits[$t.Key] = $t.Value }
                    }
                    if ($pa.Name -eq $DataFilesAttr) {
                        # Drop named-argument values (TargetPath = "ModConfig"),
                        # then every remaining quoted string is a fixture path.
                        $cleanArgs = $pa.RawArgs -replace '=\s*"[^"]*"', ''
                        foreach ($fm in [regex]::Matches($cleanArgs, '"([^"]+)"')) {
                            [void]$cls.Fixtures.Add($fm.Groups[1].Value)
                        }
                        if ($cls.Fixtures.Count -eq 0 -and $cleanArgs.Trim()) {
                            [void]$cls.Fixtures.Add($cleanArgs.Trim())
                        }
                    }
                }
                $classes.Add($cls)
                $current = $cls
                $declDepth = $depth
                $justDeclared = $true
                $pending.Clear()
                $pendingClassAttrs.Clear()
            }

            $attrMatches = [regex]::Matches($code, $ReAttr)
            if ($null -ne $current) {
                foreach ($am in $attrMatches) {
                    # Detection runs on the stripped line (proves the attribute is
                    # real code, not comment/string text); arguments are re-read
                    # from the raw line because the stripper removes string
                    # literals, and fixture paths live inside them.
                    $pending.Add([pscustomobject]@{
                        Name    = $am.Groups[1].Value
                        RawArgs = $(Get-RawAttributeArgs -RawLine $rawLine -AttrName $am.Groups[1].Value)
                    })
                }
            } else {
                foreach ($am in $attrMatches) {
                    $pendingClassAttrs.Add([pscustomobject]@{
                        Name    = $am.Groups[1].Value
                        RawArgs = $(Get-RawAttributeArgs -RawLine $rawLine -AttrName $am.Groups[1].Value)
                    })
                }
            }

            if ($null -ne $current -and $code -notmatch $ReNonMember) {
                $mm = [regex]::Match($code, $ReMethod)
                if ($mm.Success) {
                    $mn = $mm.Groups[1].Value
                    $isA = $false; $isF = $false; $isT = $false; $inlineRows = 0
                    $methodTraits = @{}
                    foreach ($a in $pending) {
                        if ($a.Name -eq $AtlasAttr) { $isA = $true }
                        elseif ($a.Name -eq $FactAttr) { $isF = $true }
                        elseif ($a.Name -eq $TheoryAttr) { $isT = $true }
                        elseif ($a.Name -eq $InlineAttr) { $inlineRows++ }
                        elseif ($a.Name -eq $TraitAttr) {
                            $t = Parse-TraitArgs -RawArgs $a.RawArgs
                            if ($null -ne $t) { $methodTraits[$t.Key] = $t.Value }
                        }
                    }
                    $pending.Clear()
                    if ($isA -or $isF -or $isT) {
                        $rows = 1
                        if ($isA) { $kind = 'Atlas' } elseif ($isF) { $kind = 'Fact' } else { $kind = 'Theory' }
                        if ($isT) {
                            $rows = $inlineRows
                            if ($rows -lt 1) { $rows = 1 }
                        }
                        $current.Methods.Add([pscustomobject]@{ Name = $mn; Rows = $rows; Kind = $kind; Traits = $methodTraits })
                    }
                }
            }

            $depth += $openCount - $closeCount
            if ($null -ne $current -and -not $justDeclared -and $depth -le $declDepth) {
                $current = $null
                $pending.Clear()
            }
        }
    }

    $withRows = @($classes | Where-Object { $_.Methods.Count -gt 0 })
    $helpers = @($classes | Where-Object { $_.Methods.Count -eq 0 })
    $testTotal = 0
    foreach ($cls in $withRows) {
        $hasAtlas = $false
        $rowSum = 0
        foreach ($m in $cls.Methods) {
            if ($m.Kind -eq 'Atlas') { $hasAtlas = $true }
            $rowSum += $m.Rows
        }
        $cls | Add-Member -NotePropertyName RowTotal -NotePropertyValue $rowSum -Force
        $cls | Add-Member -NotePropertyName Kind -NotePropertyValue $(if ($hasAtlas) { 'Atlas' } else { 'Plain' }) -Force
        $testTotal += $rowSum
    }
    $atlasCount = @($withRows | Where-Object { $_.Kind -eq 'Atlas' }).Count
    $plainCount = @($withRows | Where-Object { $_.Kind -eq 'Plain' }).Count

    return [pscustomobject]@{
        Project   = $Projects[$ProjectName]
        Label     = $ProjectName
        TestTotal = $testTotal
        ClassCount = $withRows.Count
        AtlasCount = $atlasCount
        PlainCount = $plainCount
        WithRows  = $withRows
        Helpers   = $helpers
    }
}

function Get-InventoryMarkdown {
    param($inv)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!-- AUTOGENERATED - test coverage index. Do not edit by hand. Regenerate: pwsh -File scripts\generate-test-inventory.ps1 -->')
    [void]$sb.AppendLine('# ' + $inv.Label + ' - Test Coverage Index (generated)')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Totals')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("$($inv.TestTotal) tests across $($inv.ClassCount) classes: $($inv.AtlasCount) Atlas-boot classes, $($inv.PlainCount) plain test classes.")
    if ($inv.Helpers.Count -gt 0) {
        [void]$sb.AppendLine("$($inv.Helpers.Count) non-test classes (abstract bases / harness helpers) are not counted.")
    } else {
        [void]$sb.AppendLine('No non-test classes.')
    }
    [void]$sb.AppendLine()

    $methodTotal = 0; $sumMethods = 0; $pinMethods = 0; $roleMethods = 0
    foreach ($cls in $inv.WithRows) {
        foreach ($m in $cls.Methods) {
            $methodTotal++
            if ($m.Traits.ContainsKey('summary')) { $sumMethods++ }
            if ($m.Traits.ContainsKey('pins')) { $pinMethods++ }
            if ($m.Traits.ContainsKey('role')) { $roleMethods++ }
        }
    }
    $classTotal = $inv.WithRows.Count
    $areaClasses = @($inv.WithRows | Where-Object { $_.Traits.ContainsKey('area') }).Count
    $sumClasses = @($inv.WithRows | Where-Object { $_.Traits.ContainsKey('summary') }).Count

    [void]$sb.AppendLine('## Annotation coverage')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Rows: $sumMethods/$methodTotal with ``summary``, $pinMethods/$methodTotal with ``pins``, $roleMethods/$methodTotal with ``role``.")
    [void]$sb.AppendLine("Classes: $areaClasses/$classTotal with ``area``, $sumClasses/$classTotal with ``summary``.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Class list')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| Class | Area | Rows | Kind | Summary | Fixture |')
    [void]$sb.AppendLine('|---|---|---|---|---|---|')
    foreach ($cls in $inv.WithRows) {
        $fix = $(if ($cls.Fixtures.Count -gt 0) { ($cls.Fixtures -join '; ') } else { '-' })
        $area = $(if ($cls.Traits.ContainsKey('area')) { $cls.Traits['area'] } else { '-' })
        $csum = $(if ($cls.Traits.ContainsKey('summary')) { $cls.Traits['summary'] } else { '-' })
        [void]$sb.AppendLine("| $($cls.Name) | $area | $($cls.RowTotal) | $($cls.Kind) | $csum | $fix |")
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Rows')
    [void]$sb.AppendLine()
    foreach ($cls in $inv.WithRows) {
        $headArea = $(if ($cls.Traits.ContainsKey('area')) { $cls.Traits['area'] + ', ' } else { '' })
        [void]$sb.AppendLine("### $($cls.Name) - $headArea$($cls.Kind), $($cls.RowTotal) row(s)")
        [void]$sb.AppendLine()
        $fix2 = $(if ($cls.Fixtures.Count -gt 0) { ($cls.Fixtures -join '; ') } else { '-' })
        [void]$sb.AppendLine("Fixture: $fix2")
        [void]$sb.AppendLine()
        foreach ($m in $cls.Methods) {
            $suf = $(if ($m.Kind -ne 'Atlas') { " *($($m.Kind) - $($m.Rows) row(s))*" } else { '' })
            $tags = New-Object System.Collections.ArrayList
            if ($m.Traits.ContainsKey('role')) { [void]$tags.Add($m.Traits['role']) }
            if ($m.Traits.ContainsKey('redfirst') -and $m.Traits['redfirst'] -eq 'true') { [void]$tags.Add('red-first') }
            if ($m.Traits.ContainsKey('gamemode')) { [void]$tags.Add($m.Traits['gamemode']) }
            $tagSuffix = $(if ($tags.Count -gt 0) { ' — [' + ($tags -join ', ') + ']' } else { '' })
            [void]$sb.AppendLine('- ``' + $m.Name + '``' + $suf + $tagSuffix)
            if ($m.Traits.ContainsKey('summary')) { [void]$sb.AppendLine('  ' + $m.Traits['summary']) }
            if ($m.Traits.ContainsKey('pins')) { [void]$sb.AppendLine('  pins: ' + $m.Traits['pins']) }
            if ($m.Traits.ContainsKey('evidence')) { [void]$sb.AppendLine('  evidence: ' + $m.Traits['evidence']) }
            if ($m.Traits.ContainsKey('decision')) { [void]$sb.AppendLine('  decision: ' + $m.Traits['decision']) }
            if ($m.Traits.ContainsKey('caveat')) { [void]$sb.AppendLine('  caveat: ' + $m.Traits['caveat']) }
        }
        [void]$sb.AppendLine()
    }
    if ($inv.Helpers.Count -gt 0) {
        [void]$sb.AppendLine('## Non-test classes (no rows)')
        [void]$sb.AppendLine()
        foreach ($cls in $inv.Helpers) {
            [void]$sb.AppendLine("- **$($cls.Name)** - $($cls.File)")
        }
        [void]$sb.AppendLine()
    }
    return $sb.ToString()
}

function Normalize-Markdown {
    param([string]$Content)
    return (($Content -replace "`r`n", "`n").TrimEnd("`n") + "`n")
}

# Phase-4 annotation gate: a class must carry area+summary, a row must carry
# summary. pins/role stay recommended-only (frozen in the plan). Returns the
# list of missing-annotation descriptors (empty when complete).
function Get-MissingAnnotations {
    param($inv)
    $missing = New-Object System.Collections.ArrayList
    foreach ($cls in $inv.WithRows) {
        if (-not $cls.Traits.ContainsKey('area')) { [void]$missing.Add("class $($cls.Name): missing area") }
        if (-not $cls.Traits.ContainsKey('summary')) { [void]$missing.Add("class $($cls.Name): missing summary") }
        foreach ($m in $cls.Methods) {
            if (-not $m.Traits.ContainsKey('summary')) { [void]$missing.Add("row $($cls.Name).$($m.Name): missing summary") }
        }
    }
    return $missing
}

$targets = @('CarryOn.Scenarios', 'CarryOn.Scenarios.ExampleMods')
if ($Suite -ne 'All') { $targets = @($Suite) }
$exitCode = 0

foreach ($name in $targets) {
    $inv = Get-SuiteInventory -ProjectName $name
    $md = Get-InventoryMarkdown -inv $inv
    $out = Join-Path $RepoRoot (Join-Path $Projects[$name] 'INVENTORY.md')
    $stats = "$($inv.TestTotal) tests across $($inv.ClassCount) classes ($($inv.AtlasCount) Atlas, $($inv.PlainCount) plain)"

    if ($Verify) {
        $expected = Normalize-Markdown $md
        $actual = $null
        if (Test-Path -LiteralPath $out) {
            $actual = Normalize-Markdown ([System.IO.File]::ReadAllText($out))
        }
        if ($actual -eq $expected) {
            Write-Host "OK  : $($inv.Label) - $stats - INVENTORY.md is current"
        } else {
            Write-Host "FAIL: $($inv.Label) - $stats - $out does not match"
            $exitCode = 1
        }
        $missing = Get-MissingAnnotations -inv $inv
        if ($missing.Count -gt 0) {
            Write-Host "FAIL: $($inv.Label) - annotation gate - $($missing.Count) missing:"
            foreach ($m in $missing) { Write-Host "       $m" }
            $exitCode = 1
        }
    } else {
        $enc = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($out, (Normalize-Markdown $md), $enc)
        Write-Host "WROTE: $out"
        Write-Host "       $stats"
        $missing = Get-MissingAnnotations -inv $inv
        if ($missing.Count -gt 0) {
            Write-Host "WARN : $($inv.Label) - $($missing.Count) missing annotations (fails -Verify; see the tally in INVENTORY.md)"
        }
    }
}

if ($Verify -and $exitCode -ne 0) {
    Write-Host ''
    Write-Host 'Drift detected. Regenerate: pwsh -File scripts\generate-test-inventory.ps1'
}
exit $exitCode
