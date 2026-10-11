using System.Collections.Generic;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// Phase 0 measurement for the one residual the M-band review left open:
    /// <c>BlockMultiblock.GetRotatedBlockCode</c> rebuilds the rotated block code
    /// from a hard-coded <c>"multiblock-monolithic"</c> type segment, keeping only
    /// the domain.
    /// <para>
    /// <b>Why the fixture already exists.</b> The review's precondition was "a mod
    /// whose dummy family lives outside the default domain". That is not hypothetical:
    /// <c>CarryEntityDemo/assets/game/blocktypes/multiblock-carrydemo.json</c> already
    /// registers a second <c>BlockMultiblock</c> whose <c>type</c> state is
    /// <c>carrydemo</c> rather than <c>monolithic</c>, staged by this suite for
    /// <c>WideMultiblockClaimScenarios</c>. So this needs no new fixture, and
    /// deliberately adds no <c>AtlasDataFiles</c> - a rotation lookup is engine
    /// behaviour and must not perturb the M3 rows' config or their measured
    /// carryable counts.
    /// </para>
    /// <para>
    /// <b>What is asserted.</b> <c>OnLoaded</c> derives
    /// <c>Offset = (Variant["dx"], Variant["dy"], Variant["dz"])</c> with
    /// <c>n</c>-&gt;negative and <c>p</c> stripped, so <c>carrydemo-p1-0-0</c> has
    /// <c>Offset = (1,0,0)</c>. <c>GetRotatedBlockCode</c> then returns, for 90/180/270,
    /// <c>"multiblock-monolithic"</c> + <c>OffsetToString</c> of the rotated vector.
    /// Angle 0 returns <c>this.Code</c> and is therefore correct for every type.
    /// </para>
    /// <para>
    /// <b>Why the second row exists.</b> The first row proves the returned code is in
    /// the wrong family; the second proves the consequence is a <i>silent</i>
    /// substitution rather than the engine's graceful fallback. Both call sites
    /// (<c>BlockEntityMicroBlock.RotateModel</c> and <c>GenRotatedMaterialIds</c>)
    /// fall back to the unrotated id when the rotated code does not resolve, so
    /// "resolves to the wrong block" and "falls back harmlessly" are materially
    /// different outcomes and only one of them is a defect.
    /// </para>
    /// <para>
    /// <b>These rows assert a defect, and they are expected to go red when Vintage
    /// Story fixes it.</b> Read them as a pin on current engine behaviour, not as a
    /// statement of correct behaviour. <c>GetRotatedBlockCode</c> should return a code
    /// in the block's <i>own</i> type family; when the engine derives the type segment
    /// from the block's code path instead of the <c>"multiblock-monolithic"</c>
    /// literal, both rows fail and <b>that failure is the good outcome</b>. Do not
    /// "repair" them by loosening the assertion - that is the one edit that would
    /// destroy their only purpose.
    /// </para>
    /// <para>
    /// Unlike a CarryOn defect row, these cannot be observed red-first by reverting
    /// CarryOn source: nothing in this repository reaches
    /// <c>GetRotatedBlockCode</c>. They are characterisation rows over engine code
    /// and were green on their first run on 2026-10-04.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Characterisation rows pinning an engine bug: the rotated block code is rebuilt from a hard-coded monolithic type segment, so a non-monolithic dummy family rotates into the vanilla family.")]
    public class MultiblockRotationCodeScenarios : CarryOnScenarioBase
    {
        private const string ControllerCode = "carrieddemo:wide-multiblock-block";

        /// <summary>The first dummy the controller writes: Offset = (1,0,0).</summary>
        private const string CarryDemoDummy = "game:multiblock-carrydemo-p1-0-0";

        private Block PlaceControllerAndResolveDummy(BlockPos controller, out BlockPos dummyPos)
        {
            World.SetBlock(ControllerCode, controller);
            Assert.Equal(ControllerCode, World.BlockAt(controller).Code?.ToString());

            dummyPos = controller.Copy().Add(1, 0, 0);

            // Anti-vacuity: this code cannot resolve unless the mod-owned multiblock
            // type actually registered. If the fixture silently failed, every
            // assertion below would pass for the wrong reason or fail misleadingly.
            Assert.Equal(CarryDemoDummy, World.BlockAt(dummyPos).Code?.ToString());

            var dummy = World.BlockAt(dummyPos);
            Assert.NotNull(dummy);
            return dummy!;
        }

        [Trait("summary", "For a mod-owned multiblock family, angle 0 returns the block's own code while 90, 180 and 270 return a multiblock-monolithic code in the same domain.")]
        [Trait("pins", "The engine rebuilds the rotated code from a hard-coded monolithic type segment.")]
        [Trait("role", "contract")]
        [Trait("caveat", "These rows assert a defect and are expected to go red when the engine derives the type segment from the block's own path; do not loosen the assertion.")]
        [AtlasScenario(RollbackWorld = true)]
        public void RotatedBlockCode_ForModOwnedFamily_ReturnsMonolithicDomain()
        {
            var dummy = PlaceControllerAndResolveDummy(World.Spawn.Offset(4, 1, 4), out _);

            var actual = new List<string>();
            foreach (var angle in new[] { 0, 90, 180, 270 })
            {
                actual.Add($"{angle}=>{dummy.GetRotatedBlockCode(angle)?.ToString() ?? "<null>"}");
            }

            Assert.Equal(
                "0=>game:multiblock-carrydemo-p1-0-0; " +
                "90=>game:multiblock-monolithic-0-0-p1; " +
                "180=>game:multiblock-monolithic-n1-0-0; " +
                "270=>game:multiblock-monolithic-0-0-n1",
                string.Join("; ", actual));
        }

        [Trait("summary", "The substituted monolithic code resolves to a real block, so the wrong-family swap is silent rather than falling back to the unrotated id.")]
        [Trait("pins", "The swap is silent because both call sites would otherwise fall back harmlessly to the unrotated id.")]
        [Trait("role", "contract")]
        [Trait("caveat", "These rows assert a defect and are expected to go red when the engine derives the type segment from the block's own path; do not loosen the assertion.")]
        [AtlasScenario(RollbackWorld = true)]
        public void RotatedBlockCode_SubstitutedVanillaDummy_Resolves_SoTheSwapIsSilent()
        {
            var dummy = PlaceControllerAndResolveDummy(World.Spawn.Offset(-4, 1, 4), out _);

            var rotated = dummy.GetRotatedBlockCode(90);
            Assert.NotNull(rotated);

            var resolved = World.Api.World.GetBlock(rotated);
            Assert.NotNull(resolved);
            Assert.Equal("game:multiblock-monolithic-0-0-p1", resolved!.Code?.ToString());
        }
    }
}