using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Logic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Events
{
    /// <summary>
    /// Sends a message to the player when a block is dropped.
    /// </summary>
    public class MessageOnBlockDropped : ICarryEventHandler
    {
        public void Init(ICarryManager carryManager)
        {
            if (carryManager.Api.Side != EnumAppSide.Server) return;

            carryManager.CarryEvents.BlockDropped += OnCarriedBlockDropped;
        }

        public void OnCarriedBlockDropped(object? sender, BlockDroppedEventArgs e)
        {
            var messageKey = string.Format("{0}:drop-notice{1}{2}",
                    ModId,
                    e.Destroyed ? "-destroyed" : null,
                    e.HadContents ? "-spill-contents" : null
                    );

            var player = (e.Entity as EntityPlayer)?.Player as IServerPlayer;
            if (player == null) return;

            var name = e.CarriedBlock?.ItemStack?.GetName()?.ToLower();
            var slot = e.CarriedBlock != null
                ? LocalizationHelper.GetLang(player, SlotWord(e.CarriedBlock.Slot))
                : "unknown";

            player.SendMessage(GlobalConstants.GeneralChatGroup,
                LocalizationHelper.GetLang(player, messageKey, name ?? string.Empty, slot),
                EnumChatType.Notification);
        }

        private static string SlotWord(CarrySlot slot) => slot switch
        {
            CarrySlot.Hands => "slot-hands",
            CarrySlot.Back => "slot-back",
            _ => "unknown"
        };
    }
}
