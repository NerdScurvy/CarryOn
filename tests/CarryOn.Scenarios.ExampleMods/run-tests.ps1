# Runs the CarryOn example-mods Atlas scenario suite end to end:
#   1. Release-builds CarryOnLib, CarryOn, RackEmUp, BarrelRackDemo and CarryEntityDemo
#      (regenerating the staged mod outputs the test host consumes) - the "Release-first"
#      precondition, enforced instead of documented. Warnings are errors here, so
#      the "build clean 0 warnings / 0 errors" checklist item is enforced, not just
#      observed.
#   2. Runs dotnet test on the example-mods scenario project (foodshelves, the
#      third-party dependency, is staged by the project's own build target).
#
# Usage:
#   pwsh -File run-tests.ps1
#   pwsh -File run-tests.ps1 -Filter "FullyQualifiedName~RackEmUpScenarios"
param(
    [string]$Configuration = "Release",
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"

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

# The example mods' Release builds copy their zips into the dev data install's mod
# folder, so VINTAGE_STORY_DATA must be set (the foodshelves staging reads the same
# folder - the project's build target enforces the zip's presence with its own error).
if (-not $env:VINTAGE_STORY_DATA) {
    Write-Error "VINTAGE_STORY_DATA is not set. Point it at the dev data install (e.g. C:\VintageStoryData\1.22.6-dev) and re-run."
    exit 1
}

# CarryOnLib, RackEmUp, BarrelRackDemo and CarryEntityDemo are sibling checkouts of
# the CarryOn repo (the same sibling layout the test project's ProjectReference and
# AtlasMods staging assume).
$libProject = Join-Path $PSScriptRoot "..\..\..\CarryOnLib\CarryOnLib.csproj"
$modProject = Join-Path $PSScriptRoot "..\..\CarryOn.csproj"
$rackEmUpProject = Join-Path $PSScriptRoot "..\..\..\RackEmUp\RackEmUp.csproj"
$barrelRackProject = Join-Path $PSScriptRoot "..\..\..\BarrelRackDemo\BarrelRackDemo.csproj"
$carryEntityDemoProject = Join-Path $PSScriptRoot "..\..\..\CarryEntityDemo\CarryEntityDemo.csproj"

foreach ($project in @($libProject, $modProject, $rackEmUpProject, $barrelRackProject, $carryEntityDemoProject)) {
    if (-not (Test-Path $project)) {
        Write-Error "Project not found at '$project'. Expected the checkout as a sibling of the CarryOn repo."
        exit 1
    }
}

Write-Host "== Building CarryOnLib ($Configuration) =="
dotnet build $libProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Building CarryOn ($Configuration) =="
dotnet build $modProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Building RackEmUp ($Configuration) =="
dotnet build $rackEmUpProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Building BarrelRackDemo ($Configuration) =="
dotnet build $barrelRackProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Building CarryEntityDemo ($Configuration) =="
dotnet build $carryEntityDemoProject -c $Configuration -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Running CarryOn.Scenarios.ExampleMods ($Configuration) =="
$testArgs = @("test", (Join-Path $PSScriptRoot "CarryOn.Scenarios.ExampleMods.csproj"), "-c", $Configuration)
if ($Filter) { $testArgs += @("--filter", $Filter) }
& dotnet @testArgs
exit $LASTEXITCODE
