using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Delegates;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.API.Common.Models.CarryConstants;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// CarryOnLib public-contract rows: the CheckPermissionAt priority pipeline on
    /// the event hub (ascending priority around CarryOn's internal 0, first
    /// non-null decision wins, null falls through to the default world rules),
    /// the DeserializeSafe status matrix (Ok / Empty / Failed, including the
    /// missing-block and stored-item-code recovery shapes), the ResolveLangKey
    /// hybrid rule, and the SetCarriedRaw unreadable-carry contract (occupied
    /// slot, null gameplay read, Failed protected read with the untouched tree
    /// preserved for recovery).
    /// </summary>
    [Trait("area", "Contract/lib")]
    [Trait("summary", "Rows drive the CarryOnLib public surface (the permission-check priority pipeline, the deserialization status matrix, the lang-key hybrid rule, and the raw unreadable-carry adoption seam) and assert each published contract end to end.")]
    public class LibContractScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// Permission handlers registered through RegisterCheckPermissionAt run in
        /// ascending priority order around CarryOn's internal priority-0 handler:
        /// null opinions fall through to the default world rules (pickup succeeds),
        /// a +10 authoritative false denies a pickup the default world would allow,
        /// and once a handler decides, later-priority handlers are not consulted.
        /// </summary>
        [Trait("summary", "Handlers registered through RegisterCheckPermissionAt run in ascending priority around the internal priority-0 handler: null opinions fall through to the default world rules, an authoritative false at +10 denies an otherwise-allowed pickup, and a false at -5 short-circuits before the +10 handler is consulted.")]
        [Trait("pins", "The ordering, fall-through, and first-non-null-wins contract of the permission extension point.")]
        [Trait("role", "contract")]
        [Trait("decision", "16")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CheckPermissionAt_OrdersByPriority_FirstNonNullDecides()
        {
            var events = Carry.CarryEvents;
            var log = new List<string>();
            var registered = new List<CheckPermissionAtDelegate>();

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            try
            {
                // Phase A: "no opinion" handlers at -5 and +10 - both are consulted in
                // ascending order, neither decides, the default world rules allow the pickup.
                var low = new RecordingHandler("-5", null, log);
                var high = new RecordingHandler("+10", null, log);
                events.RegisterCheckPermissionAt(-5, low.Handle);
                events.RegisterCheckPermissionAt(10, high.Handle);
                registered.Add(low.Handle);
                registered.Add(high.Handle);

                var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var fallThroughFailure);
                Assert.True(carried != null, $"Fall-through pickup failed: {fallThroughFailure}");
                Assert.Contains("-5", log);
                Assert.Contains("+10", log);
                Assert.True(log.IndexOf("-5") < log.IndexOf("+10"),
                    $"Handlers must run in ascending priority order, saw [{string.Join(", ", log)}]");

                // Free the hands slot for the denial phases.
                BlockPos downAt = World.Spawn.Offset(4, 1, 0);
                var placed = Actions.PlaceDown(player.Player, CarrySlot.Hands,
                    CarryActions.AimDownAt(downAt), out var placeFailure);
                Assert.True(placed != null, $"Place-down between phases failed: {placeFailure}");

                // Phase B: a +10 authoritative false denies a pickup the default world
                // (no claims, no reinforcement) would otherwise allow.
                log.Clear();
                var denyHigh = new RecordingHandler("+10", false, log);
                events.RegisterCheckPermissionAt(10, denyHigh.Handle);
                registered.Add(denyHigh.Handle);

                BlockPos chestB = World.Spawn.Offset(3, 1, 0);
                World.SetBlock("game:chest-east", chestB);
                var denied = Actions.PickUpIntoHands(player.Entity, chestB, out var denyFailure);
                Assert.True(denied == null, "A +10 authoritative false must deny the pickup");
                Assert.Contains("+10", log);
                Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));

                // Phase C: the first non-null decision wins - the -5 false stops the
                // chain before the +10 handler is ever consulted.
                log.Clear();
                var denyLow = new RecordingHandler("-5", false, log);
                var never = new RecordingHandler("+10", null, log);
                events.RegisterCheckPermissionAt(-5, denyLow.Handle);
                events.RegisterCheckPermissionAt(10, never.Handle);
                registered.Add(denyLow.Handle);
                registered.Add(never.Handle);

                BlockPos chestC = World.Spawn.Offset(5, 1, 0);
                World.SetBlock("game:chest-east", chestC);
                var shortCircuited = Actions.PickUpIntoHands(player.Entity, chestC, out var shortFailure);
                Assert.True(shortCircuited == null, "A -5 authoritative false must deny the pickup");
                Assert.Contains("-5", log);
                Assert.DoesNotContain("+10", log);
            }
            finally
            {
                // The event hub is mod state, not world state - always restore it.
                foreach (var handler in registered)
                {
                    events.UnregisterCheckPermissionAt(handler);
                }
            }
        }

        /// <summary>
        /// The DeserializeSafe status matrix: a serialized carried chest reads Ok with
        /// its block, stored code and block-entity data intact; a null or attribute-less
        /// slot tree reads Empty (a vacant slot, never Failed); a block stack whose id
        /// only resolves to VS's missing-block placeholder (the removed-mod shape) reads
        /// Failed; an item stack with an unresolvable id recovers Ok from its stored
        /// code, and without one reads Failed.
        /// </summary>
        [Trait("summary", "A serialized carried chest round-trips Ok with its block, stored code, and block-entity data, vacant slot trees read Empty, a block id resolving only to the missing-block placeholder reads Failed, and an item stack recovers Ok from its stored code or reads Failed without one.")]
        [Trait("pins", "The deserialization status matrix, including the placeholder shadowing the stored-block-code recovery.")]
        [Trait("role", "contract")]
        [Trait("decision", "15")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DeserializeSafe_StatusMatrix_RoundTripEmptyAndUnresolvable()
        {
            var world = World.Api.World;

            // Valid round trip: a real carried chest serializes and reads back Ok.
            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);
            var chestEntity = World.BlockEntityAt<BlockEntityGenericTypedContainer>(chestPos);
            Assert.NotNull(chestEntity);
            chestEntity!.Inventory[0].Itemstack =
                new ItemStack(world.GetItem(new AssetLocation("stick")));
            chestEntity.Inventory[0].MarkDirty();

            var carried = Carry.GetCarriedFromWorld(chestPos, CarrySlot.Hands);
            Assert.NotNull(carried);

            var tree = CarriedBlockTreeSerializer.Serialize(carried!);
            Assert.NotNull(tree);
            Assert.Equal("game:chest-east",
                tree!.GetString(AttributeKeys.CarriedBlockData.OriginalBlockCode));
            Assert.NotNull(tree[AttributeKeys.CarriedBlockData.Data] as ITreeAttribute);

            var ok = CarriedBlockTreeSerializer.DeserializeSafe(tree, world);
            Assert.True(ok.IsOk, $"Round trip must read Ok, got {ok.Status}/{ok.Reason}");
            Assert.Equal("game:chest-east", ok.Carried?.ItemStack?.Block?.Code?.ToString());
            Assert.Equal("game:chest-east", ok.Carried?.OriginalBlockCode?.ToString());
            Assert.NotNull(ok.Carried?.BlockEntityData);

            // Vacant slots: no tree at all, or a tree without a stack - both Empty.
            var noTree = CarriedBlockTreeSerializer.DeserializeSafe(null!, world);
            Assert.True(noTree.IsEmpty, "A null slot tree is a vacant slot");
            var emptyTree = CarriedBlockTreeSerializer.DeserializeSafe(new TreeAttribute(), world);
            Assert.True(emptyTree.IsEmpty, "An attribute-less slot tree is a vacant slot");

            // Removed-mod shape: a block stack whose id resolves only to VS's
            // missing-block placeholder reads Failed - never silently Ok or Empty.
            var missingBlockTree = new TreeAttribute();
            missingBlockTree.SetItemstack(AttributeKeys.CarriedBlockData.Stack,
                new ItemStack { Class = EnumItemClass.Block, Id = 999999 });
            var missingRead = CarriedBlockTreeSerializer.DeserializeSafe(missingBlockTree, world);
            Assert.True(missingRead.IsFailed,
                $"Unresolvable block id must read Failed, got {missingRead.Status}/{missingRead.Reason}");
            Assert.Equal("missing-block", missingRead.Reason);

            // Stored-code recovery on the reachable branch: an item stack whose id no
            // longer resolves is rebuilt from its stored item code.
            var itemRecoveryTree = new TreeAttribute();
            itemRecoveryTree.SetItemstack(AttributeKeys.CarriedBlockData.Stack,
                new ItemStack { Class = EnumItemClass.Item, Id = 999999 });
            itemRecoveryTree.SetString(AttributeKeys.CarriedBlockData.OriginalItemCode, "game:linensack");
            var recovered = CarriedBlockTreeSerializer.DeserializeSafe(itemRecoveryTree, world);
            Assert.True(recovered.IsOk,
                $"Item stored-code recovery must read Ok, got {recovered.Status}/{recovered.Reason}");
            Assert.Equal("game:linensack", recovered.Carried?.ItemStack?.Collectible?.Code?.ToString());

            // Same shape without a stored code to recover from: Failed.
            var itemCorruptTree = new TreeAttribute();
            itemCorruptTree.SetItemstack(AttributeKeys.CarriedBlockData.Stack,
                new ItemStack { Class = EnumItemClass.Item, Id = 999999 });
            var itemCorrupt = CarriedBlockTreeSerializer.DeserializeSafe(itemCorruptTree, world);
            Assert.True(itemCorrupt.IsFailed,
                $"Unresolvable item id must read Failed, got {itemCorrupt.Status}/{itemCorrupt.Reason}");
            Assert.Equal("unresolvable-item-id", itemCorrupt.Reason);

            await Task.CompletedTask;
        }

        /// <summary>
        /// The ResolveLangKey hybrid rule: a failure code containing a domain separator
        /// is a full lang key owned by the reporting mod and resolves verbatim; a bare
        /// code resolves under the carryon domain.
        /// </summary>
        [Trait("summary", "A failure code containing a domain separator resolves verbatim as its owner's full lang key, while a bare code resolves under the carryon domain.")]
        [Trait("pins", "Third-party failure codes localize correctly under the hybrid rule.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ResolveLangKey_HybridRule_FullKeyVerbatimBareCodePrefixed()
        {
            Assert.Equal("rackemup:shattered",
                CarryErrorHelper.ResolveLangKey("rackemup:shattered"));
            Assert.Equal("carryon:slot-not-empty",
                CarryErrorHelper.ResolveLangKey("slot-not-empty"));
            Assert.Equal(CarryConstants.GetCarryCode("slot-not-empty"),
                CarryErrorHelper.ResolveLangKey("slot-not-empty"));

            await Task.CompletedTask;
        }

        /// <summary>
        /// A present-but-unreadable carry adopted with SetCarriedRaw (the removed-mod
        /// recovery seam): the slot stays occupied for inventory and hotbar-locking
        /// purposes, gameplay reads return null instead of silently treating it as
        /// empty, the protected read reports Failed with the untouched tree preserved
        /// for later recovery, and on the server the renderable read behaves exactly
        /// like GetCarried (the render-only placeholder is a client-side substitution).
        /// </summary>
        [Trait("summary", "A present-but-unreadable carry adopted with SetCarriedRaw keeps the slot occupied, returns null from gameplay reads, reports Failed from the protected read with the untouched tree preserved, and reads like GetCarried from the renderable read on the server.")]
        [Trait("pins", "The recovery seam never silently drops carries.")]
        [Trait("role", "contract")]
        [Trait("decision", "15")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SetCarriedRaw_UnreadableCarry_StaysOccupiedAndReportsFailed()
        {
            var world = World.Api.World;

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);
            var carried = Carry.GetCarriedFromWorld(chestPos, CarrySlot.Hands);
            Assert.NotNull(carried);

            // Build the removed-mod shape: the stack id no longer resolves and no
            // stored code remains to recover from (drop the code, the legacy
            // Data.blockCode fallback and the block-entity data).
            var rawTree = CarriedBlockTreeSerializer.Serialize(carried!);
            Assert.NotNull(rawTree);
            rawTree!.SetItemstack(AttributeKeys.CarriedBlockData.Stack,
                new ItemStack { Class = EnumItemClass.Block, Id = 999999 });
            rawTree.RemoveAttribute(AttributeKeys.CarriedBlockData.OriginalBlockCode);
            rawTree.RemoveAttribute(AttributeKeys.CarriedBlockData.Data);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            bool adopted = Carry.SetCarriedRaw(player.Entity, rawTree, CarrySlot.Hands);
            Assert.True(adopted, "SetCarriedRaw must adopt the untouched tree into the vacant hands slot");

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A present-but-unreadable carry must still count as an occupied slot");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));

            var read = Carry.GetCarriedReadResult(player.Entity, CarrySlot.Hands, out var storedTree);
            Assert.True(read.IsFailed,
                $"The unreadable carry must report Failed, got {read.Status}/{read.Reason}");
            Assert.Equal("missing-block", read.Reason);
            Assert.NotNull(storedTree);
            var storedStack = storedTree!.GetItemstack(AttributeKeys.CarriedBlockData.Stack);
            Assert.True(storedStack?.Id == 999999 && storedStack.Class == EnumItemClass.Block,
                "The adopted tree must be preserved untouched for later recovery");

            Assert.Null(Carry.GetRenderableCarried(player.Entity, CarrySlot.Hands));

            Carry.RemoveCarried(player.Entity, CarrySlot.Hands);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));

            await Task.CompletedTask;
        }

        /// <summary>
        /// Named permission handler for the priority-pipeline row: appends its tag to
        /// the shared call log and returns the configured opinion (null = no opinion).
        /// </summary>
        private sealed class RecordingHandler
        {
            private readonly string tag;
            private readonly bool? decision;
            private readonly List<string> log;

            public RecordingHandler(string tag, bool? decision, List<string> log)
            {
                this.tag = tag;
                this.decision = decision;
                this.log = log;
            }

            public void Handle(EntityPlayer player, BlockPos pos, bool isReinforced, out bool? hasPermission)
            {
                log.Add(tag);
                hasPermission = decision;
            }
        }
    }
}
