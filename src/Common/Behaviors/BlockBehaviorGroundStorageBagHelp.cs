using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Interfaces;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Behaviors
{
    /// <summary>
    /// Appends CarryOn's bag-pickup interaction to the vanilla interaction-help HUD for
    /// ground storage blocks whose targeted slot holds a bag: "pick bag into hands" while
    /// free-handed. Always passes through so the vanilla per-layout help and the
    /// contained-slot "Open" line ("blockhelp-chest-open") are preserved; this behavior
    /// only ever appends its own line. Stow/place-down gestures are intentionally not
    /// advertised (they double as the swap-to-back combo and are rare in practice).
    /// </summary>
    public class BlockBehaviorGroundStorageBagHelp : BlockBehavior
    {
        public static string Name { get; } = GetCarryCode("groundStorageBagHelp");

        private static ICarryManager? carryManager;
        private static IConfigProvider? configProvider;

        /// <summary>
        /// Initializes the behavior with the carry manager for carried-state queries and
        /// config access. Must be called during mod startup on both client and server.
        /// </summary>
        public static void Init(ICarryManager manager)
        {
            carryManager = manager ?? throw new ArgumentNullException(nameof(manager));
            configProvider = manager as IConfigProvider
                ?? throw new ArgumentException("manager must implement IConfigProvider", nameof(manager));
        }

        /// <summary>
        /// Appends this behavior to every <see cref="BlockGroundStorage"/> block instance that
        /// does not already carry it. Idempotent and safe to call on both client and server
        /// (single-player shares the same block registry; dedicated-server clients call it in
        /// their AssetsFinalize mirror so the HUD sees the behavior).
        /// </summary>
        public static void InjectInto(ICoreAPI api)
        {
            for (int i = 0; i < api.World.Blocks.Count; i++)
            {
                var block = api.World.Blocks[i];
                if (block is not BlockGroundStorage) continue;
                if (block.BlockBehaviors.Any(b => b is BlockBehaviorGroundStorageBagHelp)) continue;
                block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorGroundStorageBagHelp(block)).ToArray();
            }
        }

        public BlockBehaviorGroundStorageBagHelp(Block block) : base(block)
        {
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;

            if (configProvider?.Config?.CarryOptions?.PickupGroundStorageBagWhileCarrying != true)
            {
                return [];
            }

            if (forPlayer?.Entity == null
                || selection?.Block is not BlockGroundStorage
                || selection.Position == null)
            {
                return [];
            }

            // Ground storage never carries a Carryable behavior; guard anyway so the help
            // never duplicates the generic block-carry hint if one is ever injected.
            if (selection.Block.GetBehavior<BlockBehaviorCarryable>() != null)
            {
                return [];
            }

            var beg = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityGroundStorage;
            if (!beg.IsGroundStorageBagTargeted(selection))
            {
                return [];
            }

            if (configProvider.Config.CarryOptions?.ClientSidePermissionCheck == true
                && !carryManager!.HasPermissionAt(forPlayer.Entity, selection.Position))
            {
                return [];
            }

            var carriedHands = carryManager!.GetCarried(forPlayer.Entity, CarrySlot.Hands);

            // Hands already carry something: the bag cannot be picked. No hint for the
            // stow/place-down gestures - they share the swap-to-back combo and are rare.
            if (carriedHands != null)
            {
                return [];
            }

            // Empty hands: the player can pick the targeted bag into the hands slot.
            if (!forPlayer.Entity.CanDoCarryAction(requireEmptyHanded: true))
            {
                return [];
            }

            return
            [
                new WorldInteraction
                {
                    ActionLangCode = GetCarryCode("blockhelp-bag-pickup"),
                    HotKeyCode = HotKeyCodes.Pickup,
                    MouseButton = EnumMouseButton.Right,
                    RequireFreeHand = true,
                    Itemstacks = HelpIconStacks.Get(world, "icon-handsfree")
                }
            ];
        }
    }
}