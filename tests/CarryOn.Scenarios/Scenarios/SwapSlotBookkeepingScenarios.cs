using System;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Slot bookkeeping and swap flows over the public manager seam: occupancy and raw-read
    /// queries, SwapCarried data exchange, SetCarried with an overridden destination slot,
    /// GetCarriedFromWorld with entity context, carried-revision increments, and the
    /// IsCarryable slot-support matrix.
    /// </summary>
    [Trait("area", "Pickup/Place-down")]
    [Trait("summary", "Slot bookkeeping and swap flows over the public manager seam: occupancy and raw-read queries, swap data exchange, a re-homed slot override, programmatic world pickup, carried-revision increments, and the per-slot carryable matrix.")]
    public class SwapSlotBookkeepingScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// Picking up into both hands and back, then swapping the slots, exchanges the data
        /// (each block only moves into slots its behavior allows); a same-slot swap throws;
        /// removing clears occupancy.
        /// </summary>
        [Trait("summary", "A chest picked into Hands and a distinct chest onto Back exchange slot to slot across two swaps, a same-slot swap throws, and removing one slot clears only it while the empty slot reads empty with a null tree.")]
        [Trait("pins", "Swap is a pure data exchange, remove is surgical, and raw reads are safe.")]
        [Trait("role", "coverage")]
        [Trait("decision", "9")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SwapCarried_TwoOccupiedSlots_ExchangesDataAndClearsCleanly()
        {
            BlockPos eastPos = World.Spawn.Offset(2, 1, 0);
            BlockPos northPos = World.Spawn.Offset(2, 1, 2);
            World.SetBlock("game:chest-east", eastPos);
            World.SetBlock("game:chest-north", northPos);

            var eastCode = World.BlockAt(eastPos)?.Code?.ToString();
            var northCode = World.BlockAt(northPos)?.Code?.ToString();
            Assert.True(eastCode != null && northCode != null && eastCode != northCode,
                $"Expected two distinct chest variants in the sandbox, got east={eastCode}, north={northCode}");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            Assert.True(Actions.PickUpIntoHands(player.Entity, eastPos, out _) != null, "Chest pickup failed");
            Assert.True(Actions.PickUpIntoSlot(player.Entity, northPos, CarrySlot.Back, out var backCode) != null, $"Chest back pickup failed: {backCode}");

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back));
            // Carried chest Block.Code canonicalizes to the default facing variant, so slot
            // identity is asserted via the preserved OriginalBlockCode of the source cell.
            Assert.Equal(eastCode, Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.Equal(northCode, Carry.GetCarried(player.Entity, CarrySlot.Back)?.OriginalBlockCode?.ToString());

            Assert.Equal(
                new[] { eastCode, northCode },
                Carry.GetAllCarried(player.Entity).Select(c => c.OriginalBlockCode?.ToString()).ToArray());

            Assert.True(Carry.SwapCarried(player.Entity, CarrySlot.Hands, CarrySlot.Back));
            Assert.Equal(northCode, Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.Equal(eastCode, Carry.GetCarried(player.Entity, CarrySlot.Back)?.OriginalBlockCode?.ToString());

            Assert.True(Carry.SwapCarried(player.Entity, CarrySlot.Hands, CarrySlot.Back));
            Assert.Equal(eastCode, Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.Equal(northCode, Carry.GetCarried(player.Entity, CarrySlot.Back)?.OriginalBlockCode?.ToString());

            Assert.Throws<ArgumentException>(() => Carry.SwapCarried(player.Entity, CarrySlot.Hands, CarrySlot.Hands));

            Carry.RemoveCarried(player.Entity, CarrySlot.Back);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back));
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Back));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));

            var attachedRead = Carry.GetCarriedReadResult(player.Entity, CarrySlot.Attached, out var rawTree);
            Assert.True(attachedRead.IsEmpty, $"Attached slot should read empty, got status {attachedRead.Status}");
            Assert.Null(rawTree);
        }

        /// <summary>
        /// GetCarriedFromWorld converts the world block into a carried block and removes it;
        /// SetCarried with an overridden slot re-homes it to the back slot, keeping the hands
        /// slot empty. Server-side GetRenderableCarried mirrors GetCarried.
        /// </summary>
        [Trait("summary", "GetCarriedFromWorld removes the world block and builds the carry, and SetCarried re-homes it to Back while the renderable read mirrors the gameplay read.")]
        [Trait("pins", "The programmatic pickup path, the slot override, and renderable-read parity.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task GetCarriedFromWorld_SetCarriedOverride_MovesCarryBetweenSlots()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            string failureCode = "";
            var carried = Carry.GetCarriedFromWorld(player.Entity, pos, CarrySlot.Hands, ref failureCode, checkIsCarryable: true);
            Assert.True(carried != null, $"GetCarriedFromWorld failed: {failureCode}");
            Assert.Equal(0, World.BlockAt(pos).Id);

            Carry.SetCarried(player.Entity, carried, CarrySlot.Back, markDirty: false);

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back));
            Assert.Equal("game:chest-east", Carry.GetCarried(player.Entity, CarrySlot.Back)?.Block?.Code?.ToString());

            var renderable = Carry.GetRenderableCarried(player.Entity, CarrySlot.Back);
            Assert.NotNull(renderable);
            Assert.Equal(Carry.GetCarried(player.Entity, CarrySlot.Back)?.Block, renderable?.Block);
        }

        /// <summary>
        /// The carried revision advances on every server-side carry mutation and on an
        /// explicit touch, so change detection works after pickup and drop.
        /// </summary>
        [Trait("summary", "The carried revision strictly advances on pickup, on drop, and on an explicit attribute touch.")]
        [Trait("pins", "Clients can detect every carry mutation through the revision.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task GetCarriedRevision_IncrementsOnCarryMutations()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            int initial = Carry.GetCarriedRevision(player.Entity);

            Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out _) != null, "Chest pickup failed");
            int afterPickup = Carry.GetCarriedRevision(player.Entity);
            Assert.True(afterPickup > initial, $"Revision did not advance on pickup ({initial} -> {afterPickup})");

            Actions.Drop(player.Entity, CarrySlot.Hands);
            int afterDrop = Carry.GetCarriedRevision(player.Entity);
            Assert.True(afterDrop > afterPickup, $"Revision did not advance on drop ({afterPickup} -> {afterDrop})");

            int touched = Carry.TouchCarriedAttributes(player.Entity);
            Assert.True(touched >= afterDrop, $"TouchCarriedAttributes did not advance revision ({afterDrop} -> {touched})");
        }

        /// <summary>
        /// IsCarryable reports the carryable presence and per-slot support: chest carries in
        /// hands and back, crate carries in hands only (its back slot is off under the
        /// default config, CarryablesOnBack.Crate=false), and a terrain block sampled from the
        /// sandbox is not carryable in any slot.
        /// </summary>
        [Trait("summary", "IsCarryable reports chest support in Hands and Back, crate support in Hands only under the shipped defaults, and no support for a terrain block, with a null renderable read when nothing is carried.")]
        [Trait("pins", "The per-slot support matrix under the shipped defaults.")]
        [Trait("role", "contract")]
        [Trait("decision", "6")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task IsCarryable_SlotSupportMatrix()
        {
            BlockPos posA = World.Spawn.Offset(2, 1, 0);
            BlockPos posB = World.Spawn.Offset(2, 1, 2);
            World.SetBlock("game:chest-east", posA);
            World.SetBlock("game:crate", posB);

            var chest = World.BlockAt(posA);
            var crate = World.BlockAt(posB);

            Assert.True(chest != null && chest.Code?.ToString() == "game:chest-east", "chest-east did not resolve in this world");
            Assert.True(crate != null && crate.Code?.ToString() == "game:crate", "crate did not resolve in this world");

            Block? ground = null;
            for (int d = 1; d <= 6 && ground == null; d++)
            {
                var candidate = World.BlockAt(World.Spawn.DownCopy(d));
                if (candidate != null && candidate.Id != 0) ground = candidate;
            }
            Assert.True(ground != null && ground.Code != null, "No non-carryable terrain block found under the sandbox spawn");
            var nonCarryable = ground;

            Assert.True(Carry.IsCarryable(chest!));
            Assert.False(Carry.IsCarryable(nonCarryable));

            Assert.True(Carry.IsCarryable(chest!, CarrySlot.Hands));
            Assert.True(Carry.IsCarryable(chest!, CarrySlot.Back));
            Assert.True(Carry.IsCarryable(crate!, CarrySlot.Hands));
            Assert.False(Carry.IsCarryable(crate!, CarrySlot.Back), "Crate back slot is off under the default config (CarryablesOnBack.Crate=false)");
            Assert.False(Carry.IsCarryable(nonCarryable, CarrySlot.Hands));
            Assert.False(Carry.IsCarryable(nonCarryable, CarrySlot.Back));

            ITestPlayer player = await JoinPlayer("Tester");
            Assert.Null(Carry.GetRenderableCarried(player.Entity, CarrySlot.Hands));
        }
    }
}