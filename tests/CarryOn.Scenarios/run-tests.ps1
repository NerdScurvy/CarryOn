# Runs the CarryOn Atlas scenario suite end to end:
#   1. Release-builds CarryOnLib and CarryOn (regenerating the staged mod outputs the
#      test host consumes) - the "Release-first" precondition, enforced instead of
#      documented. Warnings are errors here, so the "build clean 0 warnings /
#      0 errors" checklist item is enforced, not just observed.
#   2. Stages that install's engine assemblies into the scenario output directory
#      (every VintageStory reference is Private=false, so MSBuild copies none -
#      see Stage-Engine.ps1 for why this is load-bearing, not cosmetic).
#   3. Runs dotnet test on the scenario project.
#
# Usage:
#   pwsh -File run-tests.ps1
#   pwsh -File run-tests.ps1 -Filter "FullyQualifiedName~TwoPlayerScenarios"
#
# Fast iteration on a single class without full rebuilds can use the Atlas CLI directly:
#   atlas run --list / --filter (see https://github.com/Pixnop/Atlas) - the server boot per
#   class is the dominant cost, so filtering to one class saves the rest.
param(
    [string]$Configuration = "Release",
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Stage-Engine.ps1")

# The scenario project resolves the VintageStory assemblies via $(VINTAGE_STORY) HintPaths;
# fail fast with a readable message instead of MSBuild reference errors.
if (-not $env:VINTAGE_STORY) {
    Write-Error "VINTAGE_STORY is not set. Point it at the VintageStory install (e.g. C:\VintageStory\1.22.6) and re-run."
    exit 1
}
if (-not (Test-Path (Join-Path $env:VINTAGE_STORY "VintagestoryAPI.dll"))) {
    Write-Error "VINTAGE_STORY ('$env:VINTAGE_STORY') does not look like a VintageStory install (VintagestoryAPI.dll not found)."
    exit 1
}

# CarryOnLib is a sibling checkout of the CarryOn repo (the same layout the test project's
# ProjectReference uses: ..\..\..\CarryOnLib).
$libProject = Join-Path $PSScriptRoot "..\..\..\CarryOnLib\CarryOnLib.csproj"
$modProject = Join-Path $PSScriptRoot "..\..\CarryOn.csproj"

if (-not (Test-Path $libProject)) {
    Write-Error "CarryOnLib project not found at '$libProject'. Expected the CarryOnLib checkout as a sibling of the CarryOn repo."
    exit 1
}

Write-Host "== Building CarryOnLib ($Configuration) =="
dotnet build $libProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Building CarryOn ($Configuration) =="
dotnet build $modProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Running CarryOn.Scenarios ($Configuration) =="
# Every VintageStory reference is Private=false, so the build stages no engine
# assemblies and a clean bin\ holds mod assemblies only. Stage this install's
# engine into the output directory; the following incremental build only adds
# the scenario project's own outputs and leaves these in place.
$scenarioProject = Join-Path $PSScriptRoot "CarryOn.Scenarios.csproj"
$scenarioOut = Join-Path $PSScriptRoot "bin\$Configuration\net10.0"
Stage-EngineDeps -Install $env:VINTAGE_STORY -OutputDir $scenarioOut

$testArgs = @("test", $scenarioProject, "-c", $Configuration)
if ($Filter) { $testArgs += @("--filter", $Filter) }
& dotnet @testArgs
exit $LASTEXITCODE
