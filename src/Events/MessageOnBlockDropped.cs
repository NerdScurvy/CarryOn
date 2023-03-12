using CarryOn.API.Common;
using CarryOn.API.Event;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CarryOn.Events
{
    public class MessageOnBlockDropped : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.BlockDropped += OnCarriedBlockDropped;
        }

        public void OnCarriedBlockDropped(object sender, BlockDroppedEventArgs e)
        {
            var player = (e.Entity as EntityPlayer)?.Player as IServerPlayer;

            var name = e.CarriedBlock.ItemStack?.GetName()?.ToLower();
            var slot = Lang.Get($"{CarrySystem.ModId}:slot-{e.CarriedBlock.Slot.ToString().ToLower()}");

            player.SendMessage(GlobalConstants.GeneralChatGroup,Lang.Get($"{CarrySystem.ModId}:drop-notice", name, slot), EnumChatType.Notification);
        }
    }
}