using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// RackEmUp end-to-end rows: one of the two shipping ICarryableBlockSlotHandler
    /// implementors (the migration surface the release notes describe). RackEmUp's
    /// patch adds both its transfer behavior (rackemup:moldracktransfer) and a
    /// carryon:carryable entry with transferCode "auto" to the vanilla mold rack, so
    /// TransferLogic discovers the slot handler among the block's own behaviors.
    /// Rows drive the real server message handlers (OnPutMessage/OnTakeMessage)
    /// against a placed mold rack: putting a carried mold into an empty rack slot,
    /// taking one back out, the occupied and incompatible put rejections (with their
    /// client-visible IngameError toasts on the vanilla wire), and the silent
    /// empty-slot take rejection (FailureCodes.Default sends nothing).
    /// Mold AND MoldRack carrying are seeded on: CarryOn's own mold-rack patch gates
    /// the rack's carryable behavior on Carryables.MoldRack (default false), and
    /// RackEmUp's ungated override entry cannot escape that gate - the
    /// MultipleBehaviorResolver merge keeps the base entry's enabledCondition, so
    /// with shipped defaults the rack ends up with no carryable behavior at all and
    /// put/take silently no-op. The production setup (the dev world config) enables
    /// both; the seed mirrors it. See the suite README decisions for the finding.
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "RackEmUp end-to-end rows: drive the real put/take handlers against a placed vanilla mold rack and assert the slot and inventory truth plus the client-visible transfer toasts.")]
    [AtlasDataFiles("fixtures/ModConfig", TargetPath = "ModConfig")]
    public class RackEmUpScenarios : CarryOnScenarioBase
    {
        private const int RackSlot = 2;

        /// <summary>
        /// A carried pickaxe mold (vanilla moldrackable attribute true) put into an
        /// empty rack slot through the real put handler: the rack slot then holds the
        /// mold stack, the hands carry clears, and the mold's world cell stays empty.
        /// </summary>
        [Trait("summary", "A carried pickaxe mold put into an empty rack slot through the real put handler: the slot adopts the mold stack, the hands carry clears, and the mold's world cell stays empty.")]
        [Trait("pins", "The happy-path put surface: the slot adopts the stack, the hands clear, and the source cell vacates.")]
        [Trait("role", "smoke")]
        [Trait("decision", "18")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PutCarriedMoldOntoRack_EmptySlotAdoptsStack_HandsClear()
        {
            Assert.True(Mod.Config.Carryables.Mold, "Seed did not enable mold carrying");
            Assert.True(Mod.Config.Carryables.MoldRack, "Seed did not enable mold-rack carrying");

            var rackEntity = PlaceMoldRack();
            var mold = ResolveToolMold("pickaxe");
            Assert.True(mold.Attributes?["moldrackable"]?.AsBool() == true,
                "The pickaxe mold must carry the vanilla moldrackable attribute");

            BlockPos moldPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(mold.Code!.ToString(), moldPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, moldPos, out var pickupFailure);
            Assert.True(carried != null, $"Mold pickup failed: {pickupFailure}");

            HandlerInvoke.Put(Mod, player.Player, new PutMessage(rackEntity.Pos, RackSlot));

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A successful put must clear the hands carry");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.Equal(mold.Code.ToString(),
                rackEntity.Inventory[RackSlot].Itemstack?.Collectible?.Code?.ToString());
            Assert.Equal(0, World.BlockAt(moldPos).Id);
        }

        /// <summary>
        /// A mold sitting in a rack slot is taken back out through the real take
        /// handler: the player then carries the mold in hands (the slot handler's
        /// stack, no block-entity data for molds), and the rack slot empties.
        /// </summary>
        [Trait("summary", "A mold sitting in a rack slot is taken back out through the real take handler: the hands carry holds the mold and the rack slot empties.")]
        [Trait("pins", "The happy-path take surface: the slot empties and the hands carry holds the mold stack.")]
        [Trait("role", "smoke")]
        [Trait("decision", "18")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task TakeMoldFromRack_SlotEmpties_HandsCarryMold()
        {
            var rackEntity = PlaceMoldRack();
            var mold = ResolveToolMold("pickaxe");
            rackEntity.Inventory[RackSlot].Itemstack = new ItemStack(mold);
            rackEntity.Inventory[RackSlot].MarkDirty();
            rackEntity.MarkDirty(true);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            HandlerInvoke.Take(Mod, player.Player, new TakeMessage(rackEntity.Pos, RackSlot));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A successful take must occupy the hands carry");
            var carried = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.Equal(mold.Code.ToString(),
                carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.True(rackEntity.Inventory[RackSlot].Empty,
                "The source slot must empty on take");
        }

        /// <summary>
        /// Putting a carried mold onto an occupied rack slot rejects with the mod's
        /// occupied failure code, surfaces the IngameError toast on the vanilla wire,
        /// keeps the carry (code preserved), and leaves the slot's original stack
        /// untouched.
        /// </summary>
        [Trait("summary", "A put onto an occupied rack slot rejects with the mod's occupied failure code and a client toast, keeps the carry, and leaves the slot's original stack untouched.")]
        [Trait("pins", "The occupied rejection surfaces its mod-specific failure code as an IngameError toast and never consumes the carry.")]
        [Trait("role", "contract")]
        [Trait("decision", "18")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PutOntoOccupiedSlot_RejectsKeepsCarry()
        {
            var rackEntity = PlaceMoldRack();
            var rackMold = ResolveToolMold("pickaxe");
            rackEntity.Inventory[RackSlot].Itemstack = new ItemStack(rackMold);
            rackEntity.Inventory[RackSlot].MarkDirty();
            rackEntity.MarkDirty(true);

            var carriedMold = ResolveToolMold("shovel");
            BlockPos moldPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(carriedMold.Code!.ToString(), moldPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, moldPos, out var pickupFailure);
            Assert.True(carried != null, $"Mold pickup failed: {pickupFailure}");

            player.Client.Clear();
            HandlerInvoke.Put(Mod, player.Player, new PutMessage(rackEntity.Pos, RackSlot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode("rackemup:mold-rack-transfer-occupied"));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected put must keep the carry");
            Assert.Equal(carriedMold.Code.ToString(),
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.ItemStack?.Collectible?.Code?.ToString());
            Assert.Equal(rackMold.Code.ToString(),
                rackEntity.Inventory[RackSlot].Itemstack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// Putting a carried anvil mold (the one vanilla tooltype with
        /// moldrackable=false) onto an empty rack slot rejects with the mod's
        /// incompatible failure code and the client-visible toast, keeping the carry
        /// and leaving the slot empty.
        /// </summary>
        [Trait("summary", "A carried anvil mold, the non-moldrackable tooltype, is rejected with the incompatibility failure code and toast, keeping the carry and leaving the empty slot empty.")]
        [Trait("pins", "The incompatibility rejection is keyed on the vanilla moldrackable attribute and reports its own failure code.")]
        [Trait("role", "contract")]
        [Trait("decision", "18")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PutIncompatibleMold_AnvilMoldRejectsKeepsCarry()
        {
            var rackEntity = PlaceMoldRack();
            var anvilMold = ResolveToolMold("anvil");
            Assert.False(anvilMold.Attributes?["moldrackable"]?.AsBool() == true,
                "The anvil mold must be the non-moldrackable tooltype");

            BlockPos moldPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(anvilMold.Code!.ToString(), moldPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, moldPos, out var pickupFailure);
            Assert.True(carried != null, $"Anvil mold pickup failed: {pickupFailure}");

            player.Client.Clear();
            HandlerInvoke.Put(Mod, player.Player, new PutMessage(rackEntity.Pos, RackSlot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode("rackemup:mold-rack-transfer-incompatible"));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected put must keep the carry");
            Assert.True(rackEntity.Inventory[RackSlot].Empty,
                "The empty slot must stay empty on an incompatible put");
        }

        /// <summary>
        /// Taking from an empty rack slot rejects with FailureCodes.Default, which
        /// SendTransferFailure deliberately does not toast: the hands stay empty, the
        /// slot stays empty, and no IngameError reaches the client.
        /// </summary>
        [Trait("summary", "Taking from an empty rack slot answers the default failure code, which sends no toast: the hands stay empty, the slot stays empty, and no IngameError reaches the client.")]
        [Trait("pins", "The silent empty-slot take: the default failure code is deliberately not toasted.")]
        [Trait("role", "contract")]
        [Trait("decision", "18")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task TakeFromEmptySlot_RejectsSilently_HandsStayEmpty()
        {
            var rackEntity = PlaceMoldRack();

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            player.Client.Clear();
            HandlerInvoke.Take(Mod, player.Player, new TakeMessage(rackEntity.Pos, RackSlot));

            var wire = VanillaClientWire.Tap(player);
            Assert.Empty(wire.Errors);

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected take must not create a carry");
            Assert.True(rackEntity.Inventory[RackSlot].Empty);
        }

        private BlockEntityMoldRack PlaceMoldRack()
        {
            var rack = World.Api.World.Blocks.FirstOrDefault(b =>
                b?.Code != null && b.Code.Path == "moldrack-normal-north");
            Assert.True(rack != null, "No game:moldrack-normal-north block - RackEmUp not staged?");

            BlockPos rackPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", rackPos.AddCopy(0, 0, -1));
            World.SetBlock(rack.Code!.ToString(), rackPos);

            var entity = World.BlockEntityAt<BlockEntityMoldRack>(rackPos);
            Assert.True(entity != null, "The placed mold rack has no BlockEntityMoldRack");
            return entity!;
        }

        private Block ResolveToolMold(string toolType)
        {
            var suffix = $"-fired-{toolType}";
            var mold = World.Api.World.Blocks.FirstOrDefault(b =>
                b?.Code != null && b.Code.Path.StartsWith("toolmold-") && b.Code.Path.EndsWith(suffix));
            Assert.True(mold != null, $"No fired {toolType} tool mold block found");
            return mold!;
        }
    }
}
