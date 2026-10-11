using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Atlas runs one embedded server per test class; paths resolve relative to this test
// assembly's output directory (.../tests/CarryOn.Scenarios.ExampleMods/bin/{Debug|Release}/net10.0).
// This project sits at the same depth as the main suite (tests/<project>), so the
// counts match it exactly. Five of the staged mods are sibling checkouts whose
// Release builds produce a zip at the project's bin/ root:
//   CarryOn:        5x ".." -> CarryOn root, then bin/CarryOn.zip
//   CarryOnLib:     6x ".." -> repo parent, then CarryOnLib/bin/CarryOnLib.zip
//   RackEmUp:       6x ".." -> repo parent, then RackEmUp/bin/RackEmUp.zip
//   BarrelRackDemo: 6x ".." -> repo parent, then BarrelRackDemo/bin/BarrelRackDemo.zip
//   CarryEntityDemo: 6x ".." -> repo parent, then CarryEntityDemo/bin/CarryEntityDemo.zip
// The sixth, foodshelves (BarrelRackDemo's third-party dependency, not a checkout), is
// copied into this output under the stable name foodshelves.zip by the StageFoodShelves
// build target.
[assembly: AtlasMods(
    "../../../../../bin/CarryOn.zip",
    "../../../../../../CarryOnLib/bin/CarryOnLib.zip",
    "../../../../../../RackEmUp/bin/RackEmUp.zip",
    "../../../../../../BarrelRackDemo/bin/BarrelRackDemo.zip",
    "../../../../../../CarryEntityDemo/bin/CarryEntityDemo.zip",
    "foodshelves.zip")]
