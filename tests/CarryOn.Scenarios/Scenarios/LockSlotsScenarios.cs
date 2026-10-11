using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Network;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    [Trait("area", "Smoke")]
    [Trait("summary", "Locks the hotbar slots and asserts the server sends a LockSlotsMessage that the simulated client captures on the carryon channel.")]
    public class LockSlotsScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// LockHotbarSlots emits a LockSlotsMessage on the carryon channel, captured on the
        /// simulated client side as a real client would decode it.
        /// </summary>
        [Trait("summary", "LockHotbarSlots emits a LockSlotsMessage that the simulated client captures on the carryon channel.")]
        [Trait("pins", "Hotbar locking notifies the real client over the carryon channel and proves the packet-capture seam.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task LockHotbarSlots_SendsLockSlotsMessageToClient()
        {
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            Carry.LockHotbarSlots(player.Player);

            var packets = player.Client.Packets<LockSlotsMessage>(Channel);
            Assert.NotEmpty(packets);
        }
    }
}