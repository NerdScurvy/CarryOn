using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Atlas runs one embedded server per test class; paths resolve relative to this test
// assembly's output directory (.../tests/CarryOn.Scenarios/bin/{Debug|Release}/net10.0).
// CarryOn and CarryOnLib only copy resources/modinfo.json into their output in Release,
// and the Release build also produces a zip per mod at the project's bin/ root - the
// zip root is the mod root, and zips stage under unique names (two folders ending in
// the same segment would collide in the staging directory):
//   CarryOn:    5x ".." -> CarryOn root, then bin/CarryOn.zip
//   CarryOnLib: 6x ".." -> repo parent, then CarryOnLib/bin/CarryOnLib.zip
[assembly: AtlasMods(
    "../../../../../bin/CarryOn.zip",
    "../../../../../../CarryOnLib/bin/CarryOnLib.zip")]