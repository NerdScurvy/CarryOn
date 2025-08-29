using CarryOn.API.Common.Interfaces;
using CarryOn.API.Event.Data;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Events
{
    /// <summary>
    /// Sends a message to the player when a block is dropped.
    /// </summary>
    public class MessageOnBlockDropped : ICarryEvent
    {
        public void Init(ICarryManager carryManager)
        {
            if (carryManager.Api.Side != EnumAppSide.Server) return;

            carryManager.CarryEvents.BlockDropped += OnCarriedBlockDropped;
        }

        public void OnCarriedBlockDropped(object sender, BlockDroppedEventArgs e)
        {
            var messageKey = string.Format("{0}:drop-notice{1}{2}",
                    ModId,
                    e.Destroyed ? "-destroyed" : null,
                    e.HadContents ? "-spill-contents" : null
                    );

            var player = (e.Entity as EntityPlayer)?.Player as IServerPlayer;

            var name = e.CarriedBlock.ItemStack?.GetName()?.ToLower();
            var slot = CarrySystem.GetLang($"slot-{e.CarriedBlock.Slot.ToString().ToLower()}");

            player?.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get(messageKey, name, slot), EnumChatType.Notification);
        }
    }
}