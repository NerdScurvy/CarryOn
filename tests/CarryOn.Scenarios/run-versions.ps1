# Runs the CarryOn Atlas scenario suites across multiple Vintage Story installs
# without touching the machine's environment: VINTAGE_STORY is set per run
# inside this script's process only (the dotnet children inherit it; VS Code
# and every other terminal keep their own value - no restart, no user/machine
# env edits; if invoked from an interactive session, the finally block restores
# the original value on the way out).
#
# Usage:
#   pwsh -File run-versions.ps1
#   pwsh -File run-versions.ps1 -Suite Main
#   pwsh -File run-versions.ps1 -Suite ExampleMods -Filter "FullyQualifiedName~RackEmUpScenarios"
#   pwsh -File run-versions.ps1 -Installs "C:\VintageStory\1.22.6,C:\VintageStory\1.22.7"
#   pwsh -File run-versions.ps1 -CompileOnly
#   pwsh -File run-versions.ps1 -SummaryOnly
#
# Suites: Main (this directory) and ExampleMods (the sibling
# CarryOn.Scenarios.ExampleMods project - separate because [AtlasMods] is
# assembly-level and staging the demo mods into the main suite would perturb
# its content-pinning rows). Default is BOTH suites; -Suite exists to isolate
# a failure to one suite, not for routine use. Each suite builds into and
# tests from its own bin\<Configuration>\net10.0, but both write their TRX
# files into THIS directory's shared TestResults\ as
# carryon-<version>-<suite>.trx, so one directory holds the whole matrix. The
# pre-extension carryon-<version>.trx files (main suite, no suite suffix)
# stay comparable: -SummaryOnly reads an unsuffixed name as Main, so the
# before-picture and fresh runs coexist.
#
# Default matrix: every stable install (folder names that parse as a plain
# version - prerelease folders like "1.22.0-rc.4" drop out) beside the current
# VINTAGE_STORY (fallback C:\VintageStory), at or above the mod's modinfo game
# dependency floor (1.22.0 - pre-1.22 runs need that dependency relaxed first),
# newest first. Pass -Installs to sweep an explicit list in the order given.
#
# Build rule (Atlas, "build newest, run older"): everything is built ONCE
# against the newest install in the matrix, then every install runs
# `dotnet test --no-build` - a build against an older engine is refused by
# Atlas's preflight when run on a newer one. With the ExampleMods suite in
# scope the build covers all five mods (CarryOnLib, CarryOn, RackEmUp,
# BarrelRackDemo, CarryEntityDemo - their zips are what its [AtlasMods]
# stages, so they must be regenerated before the sweep); -Suite Main builds
# only the two the main suite stages.
#
# Two passes, always in this order: Pass A compiles the in-scope projects
# against every install (`dotnet build -t:Compile` into an isolated scratch
# under TestResults\compile-scratch\, deleted as soon as the install compiles
# clean) - seconds per install, cheap enough to always run, and a compile
# failure against an install the modinfo game floor claims to support is the
# most actionable error this script can produce. -CompileOnly stops after
# Pass A. Pass B is the runtime sweep: build once against the newest install,
# then per install re-stage the engine and `dotnet test --no-build`.
# -SummaryOnly skips both and reprints the last sweep's TRX summaries.
#
# Expect failures on other versions where rows are decompile-pinned to 1.22.x
# vanilla behavior (README "Decisions"): the sweep surfaces drift, it does
# not promise an all-green matrix. One TRX per (version, suite) lands in
# TestResults\, diffable across versions with the Atlas CLI
# (`atlas diff a.trx b.trx`). -SummaryOnly reprints the per-(version, suite)
# summary (pass/fail counts, failed test names) from those TRX files without
# rebuilding or re-running.
param(
    [string[]]$Installs = @(),
    [string[]]$Suite = @(),
    [string]$Configuration = "Release",
    [string]$Filter = "",
    [switch]$SummaryOnly,
    [switch]$CompileOnly
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Stage-Engine.ps1")

# `powershell -File` flattens "a","b" arguments into one comma-joined string;
# normalize so both that form and a real array work.
$Installs = @($Installs | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Suite = @($Suite | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Suite -contains "Both") { $Suite = @() }

# The two suites. Each resolves its own project and bin output directory the
# way its own run-tests.ps1 does; only the TRX results directory is shared
# (this directory's TestResults\, suite-suffixed filenames - see the header).
$suites = @(
    @{
        Name    = "Main"
        Project = Join-Path $PSScriptRoot "CarryOn.Scenarios.csproj"
        OutDir  = Join-Path $PSScriptRoot "bin\$Configuration\net10.0"
    }
    @{
        Name    = "ExampleMods"
        Project = Join-Path $PSScriptRoot "..\CarryOn.Scenarios.ExampleMods\CarryOn.Scenarios.ExampleMods.csproj"
        OutDir  = Join-Path $PSScriptRoot "..\CarryOn.Scenarios.ExampleMods\bin\$Configuration\net10.0"
    }
)

if ($Suite.Count -eq 0) {
    $Suite = @($suites.Name)
} else {
    $unknown = @($Suite | Where-Object { $suites.Name -notcontains $_ })
    if ($unknown.Count -gt 0) {
        Write-Error "Unknown -Suite '$($unknown -join ',')'. Valid: Main, ExampleMods, Both (or omit for both)."
        exit 1
    }
    # Canonical order (Main first) regardless of the order given.
    $Suite = @($suites.Name | Where-Object { $Suite -contains $_ })
}

# Rows whose pass/fail legitimately depends on the vanilla naming/behaviour of
# the install under test, per suite. A failure here is "report the drift", not
# "revert the mod": the row pins a string or shape the vanilla engine owns,
# and when the engine moves, the guard the row covers stops matching. Matched
# by substring against the xUnit test name, so a rename shows up as an
# unclassified failure rather than being silently absorbed here.
$decompilePinned = @{
    Main = @(
        # M4: CarryInventoryGuard.BagContentInventoryId vs
        # BlockEntityContainedBagWorkspace's className/instanceID naming.
        "GroundStorageBag_RealVanillaWrapperId_MatchesGuardHelper",
        "GroundStorageBag_OtherPlayerHasBagOpen_PickupRejected",
        "GroundStorageBag_ActorOwnOpen_PickupAllowed"
    )
    ExampleMods = @(
        # MultiblockDummyCarryableGuardScenarios: every claim rests on
        # engine-owned shape - that game:blocktypes/multiblock has no
        # behaviors key at all, that trunk-south writes exactly one dummy at
        # dx = -1, that BlockMultiblock.OnBlockRemoved leaves the controller
        # standing, and that BlockMultiblock overrides OnPickBlock to route a
        # dummy to its controller.
        "MultiblockDummyCarryableGuardScenarios",
        # MultiCarryableBasePremiseScenarios: the conditional-behaviour
        # coexistence premise the engine's strip implements.
        "MultiCarryableBasePremiseScenarios",
        # RackEmUpScenarios: BlockEntityMoldRack's vanilla shape
        # (VSSurvivalMod-owned).
        "RackEmUpScenarios",
        # Carried in from 2026-10-04-getrotatedblockcode-residual-triage: both
        # pin BlockMultiblock.GetRotatedBlockCode's hard-coded monolithic type
        # segment and are expected to go red when VS fixes it - the clearest
        # use of this classification, and why they could not be listed before
        # the list was per-suite.
        "RotatedBlockCode_ForModOwnedFamily_ReturnsMonolithicDomain",
        "RotatedBlockCode_SubstitutedVanillaDummy_Resolves_SoTheSwapIsSilent"
    )
}

$resultsDir = Join-Path $PSScriptRoot "TestResults"
$modGameFloor = [version]"1.22.0"   # resources/modinfo.json "game" dependency

function Get-InstallVersion([string]$installPath) {
    # A folder name that parses as a plain version IS the install's game
    # version; prerelease suffixes ("1.22.0-rc.4") fail the parse.
    try { return [version](Split-Path $installPath -Leaf) } catch { return $null }
}

# --- -SummaryOnly: reprint summaries from the last sweep's TRX files -----

if ($SummaryOnly) {
    $trxFiles = @(Get-ChildItem -LiteralPath $resultsDir -Filter "carryon-*.trx" -ErrorAction SilentlyContinue)
    if ($trxFiles.Count -eq 0) {
        Write-Error "No TRX results in '$resultsDir' - run the sweep first."
        exit 1
    }

    $rows = @()
    $failuresByTrx = @{}
    foreach ($trx in $trxFiles) {
        [xml]$xml = Get-Content -LiteralPath $trx.FullName
        # carryon-<version>-<suite>.trx, with the suite optional: the
        # pre-suite-suffix files are main-suite runs and stay readable.
        $base = $trx.BaseName -replace '^carryon-', ''
        # NB: the local is $suiteName, not $suite - PowerShell variables are
        # case-insensitive, so $suite IS the [string[]]$Suite param, whose
        # type constraint coerces every assignment into a one-element array
        # (which Format-Table then renders as {Main} instead of Main).
        if ($base -match '-(Main|ExampleMods)$') {
            $suiteName = $Matches[1]
            $label = $base -replace '-(Main|ExampleMods)$', ''
        } else {
            $suiteName = "Main"
            $label = $base
        }
        $c = $xml.TestRun.ResultSummary.Counters
        $mins = $null
        try {
            $mins = [math]::Round(([datetime]$xml.TestRun.Times.finish -
                [datetime]$xml.TestRun.Times.start).TotalMinutes, 1)
        } catch { }

        $failuresByTrx[$trx.Name] = @($xml.TestRun.Results.UnitTestResult |
            Where-Object { $_.outcome -ne "Passed" })
        $vSort = $null
        try { $vSort = [version]$label } catch { }
        $rows += [PSCustomObject]@{
            Version = $label
            Suite   = $suiteName
            Result  = $(if ([int]$c.failed -eq 0) { "PASS" } else { "FAIL" })
            Passed  = [int]$c.passed
            Failed  = [int]$c.failed
            Total   = [int]$c.total
            Minutes = $mins
            When    = $trx.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            Trx     = $trx.Name
            Sort    = $(if ($vSort) { $vSort } else { [version]"0.0.0" })
        }
    }

    Write-Host "== Sweep results from $resultsDir =="
    # Sort/version descending, then Suite descending (= Main before
    # ExampleMods, the canonical order).
    $rows | Sort-Object -Property @{Expression="Sort";Descending=$true}, @{Expression="Suite";Descending=$true} |
        Select-Object Version, Suite, Result, Passed, Failed, Total, Minutes, When | Format-Table -AutoSize

    $failedRows = @($rows | Where-Object { $_.Failed -gt 0 })
    foreach ($row in $failedRows) {
        Write-Host ""
        Write-Host "== Failures on $($row.Version) ($($row.Suite)) =="
        foreach ($result in $failuresByTrx[$row.Trx]) {
            $name = [string]$result.testName
            $tag = ""
            foreach ($p in $decompilePinned[$row.Suite]) {
                if ($p.Length -gt 0 -and $name.Contains($p)) { $tag = "  <-- vanilla-drift-pinned, see README"; break }
            }
            Write-Host "  [$($result.outcome)] $name$tag"
        }
    }
    if ($failedRows.Count -eq 0) { Write-Host "No failed tests in any TRX." }
    exit $(if ($failedRows.Count -gt 0) { 1 } else { 0 })
}

# --- Matrix ------------------------------------------------------------

$discoveryRoot = "C:\VintageStory"
if ($env:VINTAGE_STORY) { $discoveryRoot = Split-Path $env:VINTAGE_STORY -Parent }

if (-not $Installs -or $Installs.Count -eq 0) {
    $Installs = @(Get-ChildItem -LiteralPath $discoveryRoot -ErrorAction SilentlyContinue |
        Where-Object { $_.PSIsContainer -and (Test-Path (Join-Path $_.FullName "VintagestoryAPI.dll")) } |
        ForEach-Object {
            $v = Get-InstallVersion $_.FullName
            if ($v -and $v -ge $modGameFloor) { $_.FullName }
        } |
        Sort-Object -Property { Get-InstallVersion $_ } -Descending)
}

if ($Installs.Count -eq 0) {
    Write-Error "No Vintage Story installs at or above $modGameFloor found under '$discoveryRoot'. Pass -Installs explicitly."
    exit 1
}

foreach ($install in $Installs) {
    if (-not (Test-Path (Join-Path $install "VintagestoryAPI.dll"))) {
        Write-Error "'$install' does not look like a VintageStory install (VintagestoryAPI.dll not found)."
        exit 1
    }
}

$buildInstall = @($Installs | Where-Object { Get-InstallVersion $_ } |
    Sort-Object -Property { Get-InstallVersion $_ } -Descending)[0]
if (-not $buildInstall) { $buildInstall = $Installs[0] }

Write-Host "Sweeping $($Installs.Count) install(s) x $($Suite.Count) suite(s):"
$Installs | ForEach-Object { Write-Host "  $_" }
$Suite | ForEach-Object { Write-Host "  [suite] $_" }

# --- Build once, against the newest install in the matrix ---------------

# Same sibling layout both suites' run-tests.ps1 assume. The demo mods build
# only when the ExampleMods suite is in scope: its [AtlasMods] stages their
# zips, while the main suite stages only CarryOn + CarryOnLib - and a
# -Suite Main sweep isolating a main failure should not fail because a demo
# mod's tree is mid-change.
$libProject = Join-Path $PSScriptRoot "..\..\..\CarryOnLib\CarryOnLib.csproj"
$modProject = Join-Path $PSScriptRoot "..\..\CarryOn.csproj"
$rackEmUpProject = Join-Path $PSScriptRoot "..\..\..\RackEmUp\RackEmUp.csproj"
$barrelRackProject = Join-Path $PSScriptRoot "..\..\..\BarrelRackDemo\BarrelRackDemo.csproj"
$carryEntityDemoProject = Join-Path $PSScriptRoot "..\..\..\CarryEntityDemo\CarryEntityDemo.csproj"

$buildList = @(
    @{ Label = "CarryOnLib"; Project = $libProject; WarningsAsErrors = $true },
    @{ Label = "CarryOn"; Project = $modProject; WarningsAsErrors = $true }
)
if ($Suite -contains "ExampleMods") {
    $buildList += @(
        @{ Label = "RackEmUp"; Project = $rackEmUpProject; WarningsAsErrors = $true },
        @{ Label = "BarrelRackDemo"; Project = $barrelRackProject; WarningsAsErrors = $true },
        @{ Label = "CarryEntityDemo"; Project = $carryEntityDemoProject; WarningsAsErrors = $true }
    )
}

foreach ($b in $buildList) {
    if (-not (Test-Path $b.Project)) {
        Write-Error "$($b.Label) project not found at '$($b.Project)'. Expected the checkout as a sibling of the CarryOn repo."
        exit 1
    }
}

# Pass A compiles the same projects: the mods above plus each in-scope suite's
# test project (with both suites in scope, the seven projects).
$compileList = @($buildList)
foreach ($s in $suites) {
    if ($Suite -notcontains $s.Name) { continue }
    $compileList += @{
        Label = [IO.Path]::GetFileNameWithoutExtension($s.Project)
        Project = $s.Project
        WarningsAsErrors = $false
        IsTestProject = $true
    }
}

$origVintageStory = $env:VINTAGE_STORY
try {
    # --- Pass A: per-install compile sweep ----------------------------------
    #
    # The runtime sweep proves the shipped binary still runs on older
    # engines; this proves the SOURCE still compiles against them. A cold
    # branch calling an API newer than the modinfo game floor runs green in
    # Pass B (the branch never executes); here it fails loudly, per install.
    #
    # Scratch discipline - every mechanism below was probed before use:
    #   - -t:Compile runs the compile chain without the Build chain: the
    #     mods' Package target never fires (it would zip each install's
    #     build over the REAL bin\<mod>.zip and copy it into the dev data
    #     mods folder), and nothing is copied to any bin.
    #   - IntermediateOutputPath - not BaseOutputPath - is what -t:Compile
    #     writes. Redirecting it per install keeps the real obj's incremental
    #     state untouched: a plain -t:Compile there leaves the assembly
    #     fresh-timestamped, and the next real build would SKIP CoreCompile
    #     and ship that install's binary as if fresh (observed: an exit 0
    #     that never compiled).
    #   - Redirecting BaseIntermediateOutputPath instead breaks the SDK's
    #     default obj\** compile exclusion: stale real-obj generated sources
    #     leak into the glob as duplicate-attribute errors. The real obj
    #     stays the restore root (its content is install-independent), and
    #     the scratch lives under TestResults\, outside every project tree,
    #     so nothing leaks either way.
    #   - The mods compile into <scratch>\ref\ deliberately: a test
    #     project's ProjectReferences resolve at <its
    #     IntermediateOutputPath>ref\, and IntermediateOutputPath propagates
    #     into that resolution - so the mods' scratch assemblies are exactly
    #     the metadata the test projects compile against, per install.
    #     BuildProjectReferences=false keeps the references' own Build chains
    #     (and their Package targets) from running.
    #   - An install's scratch is deleted the moment it compiles clean; a
    #     failing install's scratch is kept and named in the error.
    $compileScratchRoot = Join-Path $resultsDir "compile-scratch"
    if (Test-Path -LiteralPath $compileScratchRoot) {
        Remove-Item -LiteralPath $compileScratchRoot -Recurse -Force
    }

    Write-Host ""
    Write-Host "== Pass A: compile sweep over $($Installs.Count) install(s) x $($compileList.Count) project(s) =="
    foreach ($install in $Installs) {
        $v = Get-InstallVersion $install
        $label = if ($v) { $v.ToString() } else { (Split-Path $install -Leaf) -replace '[^0-9A-Za-z._-]', '_' }
        $installScratch = Join-Path $compileScratchRoot $label

        Write-Host ""
        Write-Host "== Compiling against Vintage Story $label =="
        $env:VINTAGE_STORY = $install

        foreach ($cp in $compileList) {
            # Mods compile into <scratch>\ref\ - the path the test projects'
            # ProjectReferences resolve from; test projects into <scratch>\.
            $intermediate = if ($cp.IsTestProject) { "$installScratch\" } else { "$installScratch\ref\" }
            $buildArgs = @("build", $cp.Project, "-t:Compile", "-c", $Configuration,
                "-p:IntermediateOutputPath=$intermediate")
            if ($cp.WarningsAsErrors) { $buildArgs += "-p:TreatWarningsAsErrors=true" }
            if ($cp.IsTestProject) { $buildArgs += "-p:BuildProjectReferences=false" }
            & dotnet @buildArgs
            if ($LASTEXITCODE -ne 0) {
                Write-Error "$($cp.Label) failed to compile against '$install'. With modinfo.json claiming >= $modGameFloor, that is a floor violation, not engine drift. Scratch kept for inspection: $installScratch"
                exit 1
            }
        }

        Write-Host "  $label : $($compileList.Count)/$($compileList.Count) projects compiled clean"
        Remove-Item -LiteralPath $installScratch -Recurse -Force
    }

    # All installs compiled clean - their scratch dirs are already deleted;
    # drop the (now empty) root too so a clean pass leaves nothing behind.
    if (Test-Path -LiteralPath $compileScratchRoot) {
        Remove-Item -LiteralPath $compileScratchRoot -Recurse -Force
    }

    if ($CompileOnly) {
        Write-Host ""
        Write-Host "Pass A complete - skipping the runtime sweep (-CompileOnly)."
        exit 0
    }

    # --- Pass B: build once against the newest install, then the sweep -------
    Write-Host ""
    Write-Host "== Building once against the newest install: $buildInstall =="
    $env:VINTAGE_STORY = $buildInstall

    # Warnings are errors for the mods (the release-build discipline); the
    # test projects follow their own run-tests.ps1 and build plain.
    foreach ($b in $buildList) {
        Write-Host "== Building $($b.Label) ($Configuration) =="
        $buildArgs = @("build", $b.Project, "-c", $Configuration)
        if ($b.WarningsAsErrors) { $buildArgs += "-p:TreatWarningsAsErrors=true" }
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # The sweep runs dotnet test --no-build, so each suite's test project must
    # be built here too (run-tests.ps1 leaves that to dotnet test itself).
    foreach ($s in $suites) {
        if ($Suite -notcontains $s.Name) { continue }
        Write-Host "== Building the $($s.Name) suite project ($Configuration) =="
        dotnet build $s.Project -c $Configuration
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # Every VintageStory reference is Private=false, so the builds staged no
    # engine assemblies at all. Re-stage per install per suite below: without
    # it, `--no-build` runs discover zero tests, or boot against a stale
    # version's DLLs and fail every row in ~80ms (see Stage-Engine.ps1).
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
    $rows = @()
    foreach ($install in $Installs) {
        $v = Get-InstallVersion $install
        $label = if ($v) { $v.ToString() } else { (Split-Path $install -Leaf) -replace '[^0-9A-Za-z._-]', '_' }

        Write-Host ""
        Write-Host "== Vintage Story $label =="
        $env:VINTAGE_STORY = $install

        foreach ($s in $suites) {
            if ($Suite -notcontains $s.Name) { continue }

            Write-Host "== $($s.Name) suite =="
            # Re-stage per install: each suite's output directory is shared
            # across installs, so every run must overwrite the previous
            # install's assemblies.
            Stage-EngineDeps -Install $install -OutputDir $s.OutDir

            $testArgs = @("test", $s.Project, "-c", $Configuration, "--no-build",
                "--logger", "trx;LogFileName=carryon-$label-$($s.Name).trx",
                "--results-directory", $resultsDir)
            if ($Filter) { $testArgs += @("--filter", $Filter) }

            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            & dotnet @testArgs
            $code = $LASTEXITCODE
            $sw.Stop()

            $rows += [PSCustomObject]@{
                Version = $label
                Suite   = $s.Name
                Result  = $(if ($code -eq 0) { "PASS" } else { "FAIL" })
                Exit    = $code
                Minutes = [math]::Round($sw.Elapsed.TotalMinutes, 1)
                Trx     = (Join-Path $resultsDir "carryon-$label-$($s.Name).trx")
            }
        }
    }

    Write-Host ""
    Write-Host "== Version sweep summary =="
    $rows | Format-Table -AutoSize

    $failures = @($rows | Where-Object { $_.Result -ne "PASS" })
    if ($failures.Count -gt 0) {
        Write-Host "$($failures.Count) of $($rows.Count) (version, suite) run(s) failed - TRX logs in $resultsDir (atlas diff compares two)."
        Write-Host ""
        Write-Host "Rows below are decompile-pinned to vanilla naming/behaviour; a failure on one of"
        Write-Host "them means the engine moved under CarryOn (report as vanilla drift), not a mod regression:"
        foreach ($s in $suites) {
            if ($Suite -notcontains $s.Name) { continue }
            Write-Host "  [$($s.Name)]"
            $decompilePinned[$s.Name] | ForEach-Object { Write-Host "    - $_" }
        }
        exit 1
    }
    exit 0
}
finally {
    $env:VINTAGE_STORY = $origVintageStory
}
