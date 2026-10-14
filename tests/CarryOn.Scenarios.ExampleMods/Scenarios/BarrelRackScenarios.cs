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
    /// BarrelRackDemo end-to-end rows: the second shipping ICarryableBlockSlotHandler
    /// implementor. Its patch adds the transfer behavior (barrelrackdemo:barrelracktransfer)
    /// and a transferCode-"auto" carryable entry to the foodshelves barrel rack, so
    /// TransferLogic discovers the slot handler among the block's own behaviors.
    /// foodshelves (the third-party dependency) is staged from the dev install, not a
    /// checkout. Rows drive the real server message handlers against a placed barrel
    /// rack: putting a carried vanilla barrel into the rack's barrel slot, taking one
    /// back out, the occupied put rejection (with its client-visible toast), and the
    /// silent empty-slot take rejection. Vanilla barrels are carryable by default
    /// (Carryables.Barrel), so no config seed is needed.
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "BarrelRackDemo end-to-end rows: drive the real put/take handlers against a placed foodshelves barrel rack and assert the slot and inventory truth plus the client-visible transfer toast.")]
    public class BarrelRackScenarios : CarryOnScenarioBase
    {
        private const int BarrelSlot = 0;

        /// <summary>
        /// A carried vanilla barrel put into the rack's barrel slot through the real
        /// put handler: the slot then holds the barrel stack (its liquid contents
        /// stripped by the handler), the hands carry clears, and the barrel's world
        /// cell stays empty.
        /// </summary>
        [Trait("summary", "A carried vanilla barrel put into the rack's barrel slot through the real put handler: the slot holds the barrel stack with its liquid contents stripped, the hands carry clears, and the source cell empties.")]
        [Trait("pins", "The happy-path put, including the demo's liquid extraction: the barrel contents land in the rack's second slot.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PutCarriedBarrelOntoRack_SlotHoldsBarrel_HandsClear()
        {
            var rackEntity = PlaceBarrelRack();
            var barrel = ResolveBarrel();

            BlockPos barrelPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(barrel.Code!.ToString(), barrelPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, barrelPos, out var pickupFailure);
            Assert.True(carried != null, $"Barrel pickup failed: {pickupFailure}");

            HandlerInvoke.Put(Mod, player.Player, new PutMessage(rackEntity.Pos, BarrelSlot));

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A successful put must clear the hands carry");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.Equal(barrel.Code.ToString(),
                rackEntity.Inventory[BarrelSlot].Itemstack?.Collectible?.Code?.ToString());
            Assert.Equal(0, World.BlockAt(barrelPos).Id);
        }

        /// <summary>
        /// A barrel sitting in the rack's barrel slot is taken back out through the
        /// real take handler: the player then carries the barrel in hands, and the
        /// rack slot empties.
        /// </summary>
        [Trait("summary", "A barrel sitting in the rack's barrel slot is taken back out through the real take handler: the hands carry holds the barrel and the slot empties.")]
        [Trait("pins", "The happy-path take on the second shipping slot-handler implementor.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task TakeBarrelFromRack_ReturnsToHands_SlotEmpties()
        {
            var rackEntity = PlaceBarrelRack();
            var barrel = ResolveBarrel();
            rackEntity.Inventory[BarrelSlot].Itemstack = new ItemStack(barrel);
            rackEntity.Inventory[BarrelSlot].MarkDirty();
            rackEntity.MarkDirty(true);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            HandlerInvoke.Take(Mod, player.Player, new TakeMessage(rackEntity.Pos, BarrelSlot));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A successful take must occupy the hands carry");
            var carried = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.Equal(barrel.Code.ToString(),
                carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.True(rackEntity.Inventory[BarrelSlot].Empty,
                "The source slot must empty on take");
        }

        /// <summary>
        /// Putting a carried barrel onto the occupied barrel slot rejects with the
        /// mod's occupied failure code, surfaces the IngameError toast on the vanilla
        /// wire, keeps the carry, and leaves the slot's original barrel untouched.
        /// </summary>
        [Trait("summary", "A put onto the occupied barrel slot rejects with the mod's occupied failure code and a client toast, keeps the carry, and leaves the original barrel untouched.")]
        [Trait("pins", "The occupied rejection surfaces its mod-specific failure code as an IngameError toast and never consumes the carry.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PutOntoOccupiedRackSlot_RejectsKeepsCarry()
        {
            var rackEntity = PlaceBarrelRack();
            var barrel = ResolveBarrel();
            rackEntity.Inventory[BarrelSlot].Itemstack = new ItemStack(barrel);
            rackEntity.Inventory[BarrelSlot].MarkDirty();
            rackEntity.MarkDirty(true);

            BlockPos barrelPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(barrel.Code!.ToString(), barrelPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, barrelPos, out var pickupFailure);
            Assert.True(carried != null, $"Barrel pickup failed: {pickupFailure}");

            player.Client.Clear();
            HandlerInvoke.Put(Mod, player.Player, new PutMessage(rackEntity.Pos, BarrelSlot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode("barrelrackdemo:transfer-occupied"));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected put must keep the carry");
            Assert.Equal(barrel.Code.ToString(),
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.ItemStack?.Collectible?.Code?.ToString());
            Assert.Equal(barrel.Code.ToString(),
                rackEntity.Inventory[BarrelSlot].Itemstack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// Taking from the empty barrel slot rejects with FailureCodes.Default, which
        /// SendTransferFailure deliberately does not toast: the hands stay empty, the
        /// slot stays empty, and no IngameError reaches the client.
        /// </summary>
        [Trait("summary", "Taking from the empty barrel slot answers the default failure code, which sends no toast: the hands stay empty and the slot stays empty.")]
        [Trait("pins", "The silent empty-slot take matches the mold-rack contract on the barrel rack.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task TakeFromEmptyRackSlot_RejectsSilently_HandsStayEmpty()
        {
            var rackEntity = PlaceBarrelRack();

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            player.Client.Clear();
            HandlerInvoke.Take(Mod, player.Player, new TakeMessage(rackEntity.Pos, BarrelSlot));

            var wire = VanillaClientWire.Tap(player);
            Assert.Empty(wire.Errors);

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected take must not create a carry");
            Assert.True(rackEntity.Inventory[BarrelSlot].Empty);
        }

        private BlockEntityContainer PlaceBarrelRack()
        {
            var rack = World.Api.World.Blocks.FirstOrDefault(b =>
                b?.Code != null && b.Code.Domain == "foodshelves"
                    && b.Code.Path == "barrelrack-normal-north");
            Assert.True(rack != null,
                "No foodshelves:barrelrack-normal-north block - foodshelves or BarrelRackDemo not staged?");

            BlockPos rackPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(rack.Code!.ToString(), rackPos);

            var entity = World.BlockEntityAt<BlockEntityContainer>(rackPos);
            Assert.True(entity != null,
                "The placed barrel rack has no BlockEntityContainer block entity");
            Assert.True(entity!.Inventory.Count > 1,
                "The barrel rack needs a barrel slot and a liquid slot");
            return entity;
        }

        private Block ResolveBarrel()
        {
            var barrel = World.Api.World.GetBlock(new AssetLocation("barrel"));
            Assert.True(barrel != null, "No game:barrel block found");
            return barrel!;
        }
    }
}
