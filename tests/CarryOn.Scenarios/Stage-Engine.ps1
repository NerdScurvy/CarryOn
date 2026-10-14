# Stages the Vintage Story assemblies a built scenario assembly needs, into one
# output directory. Dot-source this from run-tests.ps1 / run-versions.ps1:
#
#   . (Join-Path $PSScriptRoot "Stage-Engine.ps1")
#   Stage-EngineDeps -Install $env:VINTAGE_STORY -OutputDir $scenarioOut
#
# Why it exists: every VintageStory reference in the scenario project is
# `<Private>false</Private>` (the engine must be the one that is actually running,
# not a stale copy under test\bin). MSBuild therefore resolves them at compile
# time and copies nothing, so a freshly-built output directory has **no engine
# assemblies at all**. Two failure modes follow, and both are actively
# misleading:
#
#   1. nothing staged  -> "could not find dependent assembly 'VintagestoryAPI,
#      Version=x.y.z'" and the run discovers **zero** tests, which reads as
#      "all green, nothing to do";
#   2. the WRONG version staged -> the engine host boots against a mismatched
#      VintagestoryAPI and every row in every class fails in ~80 ms, which reads
#      as a catastrophic regression.
#
# Neither is a CarryOn defect, so the runners stage the current install's
# assemblies explicitly instead of relying on whatever a previous run left
# behind.
function Stage-EngineDeps {
    param(
        [Parameter(Mandatory = $true)][string]$Install,
        [Parameter(Mandatory = $true)][string]$OutputDir
    )

    if (-not (Test-Path -LiteralPath $Install)) {
        Write-Error "VintageStory install '$Install' does not exist."
        exit 1
    }
    if (-not (Test-Path -LiteralPath $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    # Top-level engine assemblies (VintagestoryAPI/Lib/Vintagestory/Server), plus
    # their .pdbs. The pdbs are NOT optional: the game's logger derives source
    # paths from pdb debug info during static initialization, so a VintagestoryAPI.dll
    # without VintagestoryAPI.pdb boots into an opaque TypeInitializationException
    # (NullReferenceException in LoggerBase..cctor). Atlas preflights for this and
    # refuses to start, but the underlying reason is worth stating here.
    Copy-Item -Path (Join-Path $Install "*.dll") -Destination $OutputDir -Force
    Copy-Item -Path (Join-Path $Install "*.pdb") -Destination $OutputDir -Force

    # Lib: the native payloads (libSkiaSharp, cairo, OpenTK) plus the managed
    # third-party assemblies the mod resolves at runtime. Missing natives do not
    # fail at load - they fail in a finalizer, aborting the whole test run.
    Copy-Item -Path (Join-Path $Install "Lib\*.dll") -Destination $OutputDir -Force

    $apiPdb = Join-Path $OutputDir "VintagestoryAPI.pdb"
    if (-not (Test-Path -LiteralPath $apiPdb)) {
        Write-Error "VintagestoryAPI.pdb was not staged into '$OutputDir' from '$Install'."
        exit 1
    }

    # The two gameplay mods the scenarios instantiate real block entities from.
    foreach ($mod in @("VSEssentials.dll", "VSSurvivalMod.dll")) {
        $path = Join-Path $Install "Mods\$mod"
        if (Test-Path -LiteralPath $path) {
            Copy-Item -LiteralPath $path -Destination (Join-Path $OutputDir $mod) -Force
        } else {
            Write-Error "'$mod' not found under '$Install\Mods'."
            exit 1
        }
    }

    $staged = (Get-ChildItem -LiteralPath $OutputDir -Filter "*.dll").Count
    Write-Host "  staged engine deps for $(Split-Path $Install -Leaf) into $OutputDir ($staged dlls)"
}
