using System;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// M3's missing reachability: the inventory-open guard's
    /// <c>CarryFootprint.ResolveController</c> resolution, driven through the
    /// <b>production</b> <c>Actions.PickUpIntoHands</c> path on a multiblock
    /// <b>dummy</b> cell that is itself carryable.
    /// <para>
    /// <c>PickupOpenGuardScenarios</c> row 9 pins why this could not be reached
    /// before: <c>TryPickUp</c> ordered the carryable gate ahead of the guard, so a
    /// pickup aimed at a dummy died at <c>NotCarryable</c> and the guard never ran.
    /// No vanilla dummy is carryable. Row 10 then reached the guard only through the
    /// internal <c>checkIsCarryable: false</c> seam that <b>no production caller
    /// uses</b>, so the M3 resolution had no evidence on any real path.
    /// </para>
    /// <para>
    /// <b>Why it lives in the ExampleMods suite.</b> Making a vanilla dummy
    /// carryable means patching <c>game:blocktypes/multiblock</c>, which changes the
    /// managed carryability set of every class-world in the suite that stages
    /// <c>CarryEntityDemo</c> and would perturb the main suite's content-pinning
    /// rows. The main suite's own census row treats "which codes report carryable"
    /// as pinned content, so the fixture has to stay out of it. Staging is therefore
    /// what proves the other half of the story: the main suite's vanilla rows stay
    /// green precisely <i>because</i> it never sees this patch.
    /// </para>
    /// <para>
    /// <b>Fixture, and why it is pathological.</b>
    /// <c>CarryEntityDemo/resources/assets/carrieddemo/patches/multiblock-dummy-carryable.json</c>
    /// adds an unconditional Hands carryable to every vanilla <c>multiblock</c>
    /// dummy. No real mod would ship that - it is deliberately broad, and it exists
    /// only so this class has a window to aim at. It must never be copied into a
    /// real mod.
    /// </para>
    /// <para>
    /// The patch uses <c>"path": "/behaviors"</c> with the whole array as
    /// <c>value</c>. The obvious <c>"/behaviors/-"</c> append is a <b>silent
    /// no-op</b> here, because <c>game:blocktypes/multiblock</c> has no
    /// <c>behaviors</c> key for the patcher to append to - no warning, no error, and
    /// a census byte-identical to the unpatched one. P1 exists to make that
    /// failure loud.
    /// </para>
    /// <para>
    /// The <c>MultiblockDummyConfig</c> fixture sets <c>Carryables.ChestTrunk</c>
    /// because it defaults to <c>false</c>, which leaves CarryOn's own trunk
    /// carryable - it carries <c>enabledCondition:
    /// carryon.Carryables.ChestTrunk</c> - stripped during boot conditioning. That
    /// would make C3 a second D-row.
    /// </para>
    /// <para>
    /// <b>Blast radius, measured.</b> The unguarded window lets a player punch a
    /// one-cell hole in a trunk someone else is using; it does not destroy the
    /// trunk and loses no items. <c>BlockBehaviorMultiblock.OnBlockRemoved</c>'s
    /// sweep does <i>not</i> take the controller with it. C1 therefore asserts only
    /// that the guard did not fire, never what the pickup produced.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Inventory-open guard reachability on a carryable multiblock dummy: a pickup targeted at a dummy must resolve the controller and reject with slot-inventory-open when another player has its GUI open.")]
    [AtlasDataFiles("fixtures/MultiblockDummyConfig", TargetPath = "ModConfig")]
    public class MultiblockDummyCarryableGuardScenarios : CarryOnScenarioBase
    {
        private const string TrunkSouth = "game:trunk-south";

        /// <summary>The one dummy <c>trunk-south</c> writes, one cell west.</summary>
        private const string TrunkDummySouthWest = "game:multiblock-monolithic-n1-0-0";

        /// <summary>
        /// A dummy from the <i>other</i> registered family, which the patch must not
        /// touch. <c>game:blocktypes/multiblock</c> is vanilla's
        /// <c>monolithic</c> family; this code lives in
        /// <c>CarryEntityDemo/assets/game/blocktypes/multiblock-carrydemo.json</c>
        /// (<c>WideMultiblockClaimScenarios</c>'s vehicle). It is the cross-family
        /// leak guard: the patch keys on one blocktype file, so the other must stay
        /// non-carryable or the fixture would be perturbing more than it claims.
        /// </summary>
        private const string CarryDemoDummy = "game:multiblock-carrydemo-p1-0-0";

        private const string FixturePatch =
            "CarryEntityDemo/resources/assets/carrieddemo/patches/multiblock-dummy-carryable.json";

        /// <summary>
        /// Places <c>game:trunk-south</c> and pins the two facts every row here
        /// rests on: the code landed, and the engine wrote exactly one dummy one cell
        /// west. The server accessor's <c>SetBlock</c> runs
        /// <c>BlockBehaviorMultiblock.OnBlockPlaced</c> synchronously, so the dummy
        /// is readable on the next line - <c>WideMultiblockClaimScenarios</c>
        /// .PlaceWideFixture pins the same contract for the mod-owned family.
        /// </summary>
        private BlockPos PlaceTrunk(out BlockPos dummy)
        {
            var controller = World.Spawn.Offset(6, 1, 0);
            World.SetBlock(TrunkSouth, controller);
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());

            dummy = controller.Copy().Add(-1, 0, 0);
            Assert.Equal(TrunkDummySouthWest, World.BlockAt(dummy).Code?.ToString());

            return controller;
        }

        private async Task<(ITestPlayer Actor, ITestPlayer Opener)> JoinActorAndOpenerAsync()
        {
            var actor = await JoinPlayer("Tester");
            var opener = await JoinPlayer("Opener");
            await actor.TeleportTo(World.Spawn.Offset(8, 0, 2));
            await opener.TeleportTo(World.Spawn.Offset(10, 0, 2));
            return (actor, opener);
        }

        /// <summary>
        /// P1 - <b>premise, green before and after.</b> The anti-vacuity keystone:
        /// every other row in this class is a way D1 could pass for the wrong reason,
        /// so all of it is asserted from the <b>world</b> rather than from the fixture
        /// JSON.
        /// <list type="bullet">
        /// <item><b>F1</b> the targeted cell really is a <c>BlockMultiblock</c> dummy
        /// whose <c>OffsetInv</c> resolves back to the controller;</item>
        /// <item><b>F2</b> the dummy cell exposes <b>no</b>
        /// <c>BlockEntityContainer</c> - which is why the pre-fix guard, reading
        /// <c>GetBlockEntity(pos)</c>, had nothing to match and could only fail
        /// open;</item>
        /// <item><b>F3</b> the controller <i>is</i> a container, so there is a
        /// controller-side GUI for an opener to bind and the guard has something to
        /// resolve <i>to</i>;</item>
        /// <item><b>F4</b> the dummy is carryable in Hands - the premise that makes
        /// the whole window reachable;</item>
        /// <item><b>F5</b> a dummy from the other registered family is still not
        /// carryable, so the patch did not leak.</item>
        /// </list>
        /// If a future VS release gave dummy cells their own container, F2 fails and
        /// this plan's whole argument needs revisiting - hence asserting it rather
        /// than assuming it.
        /// </summary>
        [Trait("summary", "Anti-vacuity premise: the targeted cell is a multiblock dummy resolving to a container controller that exposes no container at the dummy, the dummy is carryable in Hands, and a dummy from the other family is not.")]
        [Trait("pins", "The targeted dummy cell is carryable and resolves to a container controller, and the patch did not leak across blocktype families.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DummyCell_PatchApplied_ResolvesToAContainerController_P1()
        {
            var controller = PlaceTrunk(out var dummy);

            var dummyBlock = World.BlockAt(dummy);
            var dummyMulti = dummyBlock as BlockMultiblock;
            Assert.True(dummyMulti != null,
                $"The trunk's west cell is a {dummyBlock?.Code}, not a BlockMultiblock dummy. "
                + "Every row in this class targets that cell, so the premise no longer holds.");
            Assert.Equal(controller, dummy.Copy().Add(dummyMulti!.OffsetInv));

            Assert.True(
                World.BlockEntityAt<BlockEntityContainer>(dummy) == null,
                $"The trunk's dummy cell now exposes a BlockEntityContainer "
                + $"({dummyBlock?.Code}). The pre-fix guard read GetBlockEntity(pos) at the "
                + "clicked cell, so it would no longer have been a guaranteed no-op there, "
                + "and M3's resolution would no longer be the thing under test.");

            var controllerEntity = World.BlockEntityAt<BlockEntityContainer>(controller);
            Assert.True(
                controllerEntity != null,
                $"The trunk controller exposes no BlockEntityContainer "
                + $"({World.BlockAt(controller).Code}), so there is no controller-side GUI "
                + "to bind and the guard's resolution cannot be exercised. Pick a "
                + "multiblock whose controller is a container.");

            Assert.True(
                Carry.IsCarryable(dummyBlock!, CarrySlot.Hands),
                $"The trunk's dummy cell is not carryable in Hands. The fixture patch "
                + $"{FixturePatch} did not take effect - check that it targets "
                + "\"path\": \"/behaviors\" with the whole array as value. Note that the "
                + "\"/behaviors/-\" append form is a SILENT no-op against "
                + "game:blocktypes/multiblock, which has no \"behaviors\" key at all, so "
                + "a broken patch looks exactly like a closed window.");

            var otherFamily = World.Api.World.GetBlock(new AssetLocation(CarryDemoDummy));
            Assert.NotNull(otherFamily);
            Assert.False(
                Carry.IsCarryable(otherFamily, CarrySlot.Hands),
                $"{CarryDemoDummy} is carryable, but it belongs to the mod-registered "
                + "game:blocktypes/multiblock-carrydemo family, not the vanilla "
                + "game:blocktypes/multiblock file the patch targets. The patch has leaked "
                + "across blocktype files and this fixture is perturbing more than it claims.");

            await JoinActorAndOpenerAsync();
        }

        /// <summary>
        /// D1 - <b>RED before the fix, green after.</b> The defect row: a
        /// dummy-targeted pickup while another player has the trunk's GUI open.
        /// <para>
        /// The whole content of M3 is the resolution. Pre-fix the guard read
        /// <c>GetBlockEntity(pos)</c> at the clicked dummy cell; F2 pins that this is
        /// null, so the guard had no container to match and the pickup went through
        /// with the controller's GUI open on another player. Post-fix it resolves the
        /// controller first and blocks with <c>slot-inventory-open</c>.
        /// </para>
        /// <para>
        /// Driven through <see cref="CarryActions.PickUpIntoHands"/> - the production
        /// path, with no <c>checkIsCarryable</c> argument anywhere. That is the
        /// difference from row 10: this row needs no internal seam because P1's F4
        /// makes the carryable gate pass on its own.
        /// </para>
        /// <para>
        /// Red-first mutation: replace
        /// <c>CarryFootprint.ResolveController(entity.World, pos, block, out _)</c> at
        /// <c>CarryPickupService.cs:150</c> with the raw <c>pos.Copy()</c>, leaving
        /// everything else in place. D1 then fails with row 10's text -
        /// <i>"the guard resolved no container at a dummy cell (silent fail-open)"</i>.
        /// </para>
        /// </summary>
        [Trait("summary", "A dummy-targeted pickup while another player has the controller's GUI open must be rejected with slot-inventory-open, reached through the production pickup path with no test seam.")]
        [Trait("pins", "The guard resolves the controller from a dummy cell instead of failing open.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, where the guard read no container at the dummy cell and the pickup succeeded with the GUI open.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DummyTargetedPickup_AnotherPlayerHasControllerOpen_Rejected_D1()
        {
            var controller = PlaceTrunk(out var dummy);
            var controllerEntity = World.BlockEntityAt<BlockEntityContainer>(controller);
            Assert.True(controllerEntity != null,
                "The trunk controller exposes no BlockEntityContainer; there is no "
                + "controller-side GUI to bind and this row cannot test the guard.");

            var (actor, opener) = await JoinActorAndOpenerAsync();

            // The controller's GUI - the only GUI a player interacting with this
            // structure could have open.
            OpenFor(opener, controllerEntity!.Inventory);

            var carried = Actions.PickUpIntoHands(actor.Entity, dummy, out var failureCode);

            Assert.True(carried == null,
                "The dummy-targeted pickup succeeded while the controller's inventory was "
                + "open on another player - the guard resolved no container at a dummy cell "
                + "(silent fail-open).");
            Assert.Equal(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());
            Assert.Equal(TrunkDummySouthWest, World.BlockAt(dummy).Code?.ToString());
        }

        /// <summary>
        /// C1 - <b>control, green before and after.</b> Nothing open: the guard must
        /// not fire. This is the row that would catch a guard which blanket-rejects
        /// dummy cells.
        /// <para>
        /// It asserts <b>only</b> the negative. Measurement 2 recorded what the
        /// pickup actually does - it succeeds, the dummy cell clears to air, the
        /// controller survives with its inventory intact, and the actor is handed
        /// <c>game:trunk-east</c> with
        /// <c>OriginalBlockCode = game:multiblock-monolithic-n1-0-0</c>, because
        /// <c>BlockMultiblock</c> overrides <c>OnPickBlock</c> and routes the dummy
        /// to its controller. Pinning that as an expected outcome would assert an
        /// accident of the engine's drop rules rather than anything the guard
        /// promises, and would break the day those rules change.
        /// </para>
        /// <para>
        /// It also deliberately does <b>not</b> assert <c>failure != ""</c> as a proxy
        /// for success: <c>GetCarriedFromWorld</c> returns null without assigning a
        /// failure code when the block's pick form is not carryable, so an empty
        /// string can mean "rejected for no reported reason" rather than "allowed".
        /// </para>
        /// </summary>
        [Trait("summary", "With nothing open the guard must not fire, so the dummy-targeted pickup succeeds; the row asserts only the negative, never the pickup's outcome.")]
        [Trait("pins", "The guard does not blanket-reject dummy cells.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DummyTargetedPickup_NobodyHasItOpen_NotBlockedByTheGuard_C1()
        {
            var controller = PlaceTrunk(out var dummy);

            ITestPlayer actor = await JoinPlayer("Tester");
            await actor.TeleportTo(World.Spawn.Offset(8, 0, 2));

            var carried = Actions.PickUpIntoHands(actor.Entity, dummy, out var failureCode);

            Assert.NotEqual(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.True(carried != null,
                "With nothing open the guard must not fire, so the dummy-targeted pickup "
                + $"has to succeed. It failed with '{failureCode}', which means either the "
                + "guard is over-blocking dummy cells or the fixture is not reachable "
                + $"(see P1, and {FixturePatch}).");
        }

        /// <summary>
        /// C2 - <b>control, green before and after.</b> The acting player has the
        /// controller's GUI open on their own client and is the one who picks up the
        /// dummy: the guard skips the actor's own UID by design, so it must not fire.
        /// Mirrors <c>PickupOpenGuardScenarios</c> row 4 and keeps that self-skip half
        /// of the guard honest on the multiblock path.
        /// <para>
        /// One player, not two. The tempting version - "one player has it open, a
        /// second player picks up" - is <b>not</b> this control: from the guard's point
        /// of view the picker is the bystander, their GUI belongs to another player,
        /// and <c>slot-inventory-open</c> is the correct answer. That variant is D1
        /// again. The self-skip rule only has a case when the same player is both.
        /// </para>
        /// </summary>
        [Trait("summary", "The acting player with the controller's own GUI open must never block their own pickup: the guard skips the actor's own UID.")]
        [Trait("pins", "The guard's self-skip applies on the multiblock path.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DummyTargetedPickup_ActorOwnInventoryOpen_NotBlocked_C2()
        {
            var controller = PlaceTrunk(out var dummy);
            var controllerEntity = World.BlockEntityAt<BlockEntityContainer>(controller);
            Assert.True(controllerEntity != null,
                "The trunk controller exposes no BlockEntityContainer; there is no "
                + "controller-side GUI to bind and this row cannot test the guard.");

            ITestPlayer actor = await JoinPlayer("Tester");
            await actor.TeleportTo(World.Spawn.Offset(8, 0, 2));

            OpenFor(actor, controllerEntity!.Inventory);

            var carried = Actions.PickUpIntoHands(actor.Entity, dummy, out var failureCode);

            Assert.NotEqual(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.True(carried != null,
                "The acting player's own GUI must never block a pickup, so the "
                + $"dummy-targeted pickup has to succeed. It failed with '{failureCode}'. "
                + $"See P1, and {FixturePatch}.");
        }

        /// <summary>
        /// C3 - <b>control, green before and after.</b> The plain path: target the
        /// <b>controller</b> cell instead of the dummy, and the same guard must
        /// reject. Duplicates <c>PickupOpenGuardScenarios</c> row 3 on purpose - it
        /// is the cheapest possible check that the fixture has not perturbed the
        /// ordinary single-block-container path.
        /// <para>
        /// This row only has teeth because of the <c>MultiblockDummyConfig</c>
        /// fixture. <c>Carryables.ChestTrunk</c> defaults to <c>false</c>, so without
        /// it the trunk is not carryable at all and this row would reach the guard
        /// but die at <c>NotCarryable</c> - a second D-row that proves nothing about
        /// the plain path.
        /// </para>
        /// </summary>
        [Trait("summary", "Targeting the controller instead of the dummy with another player's GUI open still rejects slot-inventory-open, showing the fixture did not perturb the plain single-block-container path.")]
        [Trait("pins", "The ordinary container pickup path is unchanged by the fixture.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ControllerTargetedPickup_AnotherPlayerHasItOpen_Rejected_C3()
        {
            var controller = PlaceTrunk(out _);
            var controllerEntity = World.BlockEntityAt<BlockEntityContainer>(controller);
            Assert.True(controllerEntity != null,
                "The trunk controller exposes no BlockEntityContainer; there is no "
                + "controller-side GUI to bind and this row cannot test the guard.");

            Assert.True(
                Carry.IsCarryable(World.BlockAt(controller), CarrySlot.Hands),
                $"The trunk controller is not carryable in Hands, so this row cannot reach "
                + $"the guard. Carryables.ChestTrunk defaults to false - the "
                + "fixtures/MultiblockDummyConfig seed must be attached to this class.");

            var (actor, opener) = await JoinActorAndOpenerAsync();

            OpenFor(opener, controllerEntity!.Inventory);

            var carried = Actions.PickUpIntoHands(actor.Entity, controller, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());
        }
    }
}