using CarryOn.API.Common;
using CarryOn.API.Event.Data;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CarryOn.Events
{
    public class MessageOnBlockDropped : ICarryEvent
    {
        public void Init(ModSystem modSystem)
        {
            if (modSystem is not CarrySystem carrySystem) return;
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.BlockDropped += OnCarriedBlockDropped;
        }

        public void OnCarriedBlockDropped(object sender, BlockDroppedEventArgs e)
        {
            var messageKey = string.Format("{0}:drop-notice{1}{2}",
                    CarrySystem.ModId,
                    e.Destroyed ? "-destroyed" : null,
                    e.HadContents ? "-spill-contents" : null
                    );

            var player = (e.Entity as EntityPlayer)?.Player as IServerPlayer;

            var name = e.CarriedBlock.ItemStack?.GetName()?.ToLower();
            var slot = Lang.Get($"{CarrySystem.ModId}:slot-{e.CarriedBlock.Slot.ToString().ToLower()}");

            player.SendMessage(GlobalConstants.GeneralChatGroup,Lang.Get(messageKey, name, slot), EnumChatType.Notification);
        }
    }
}